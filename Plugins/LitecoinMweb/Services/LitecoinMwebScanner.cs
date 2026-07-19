using BTCPayServer.Plugins.LitecoinMweb.Configuration;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcMwebdClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Plugins.LitecoinMweb.Services
{
    public class LitecoinMwebScanner(
        EventAggregator eventAggregator,
        LitecoinMwebConfiguration configuration,
        ILogger<LitecoinMwebScanner> logger) : IHostedService
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<ByteString, Task> _scanners = [];

        public Task StartAsync(CancellationToken cancellation)
        {
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellation)
        {
            _cts.Cancel();
            foreach (var task in _scanners.Values)
            {
                try
                {
                    await task.WaitAsync(cancellation);
                }
                // A scan task may have faulted (e.g. mwebd was unavailable). Never let
                // shutdown throw over it, or BTCPay can flag the plugin as crashing.
                catch (Exception) { }
            }
            _scanners.Clear();
        }

        public void StartScan(string scanKey, int height)
        {
            var key = ByteString.CopyFrom(Convert.FromHexString(scanKey));
            if (_scanners.ContainsKey(key)) return;
            _scanners[key] = StartScan(key, height, _cts.Token);
        }

        private async Task StartScan(ByteString scanKey, int height, CancellationToken cancellation)
        {
            // Keep scanning across transient mwebd outages. If the daemon is cold
            // (e.g. right after a full-stack restart) the gRPC call throws
            // RpcException(Unavailable); wait and retry rather than letting the task
            // fault permanently, which would silently stop detecting payments.
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    using var channel = GrpcChannel.ForAddress(configuration.DaemonRpcUri);
                    var client = new Rpc.RpcClient(channel);
                    using var call = client.Utxos(new UtxosRequest
                    {
                        FromHeight = height,
                        ScanSecret = scanKey
                    }, cancellationToken: cancellation);
                    await foreach (var utxo in call.ResponseStream.ReadAllAsync(cancellation))
                    {
                        if (utxo.OutputId == "") continue;
                        eventAggregator.Publish(utxo);
                    }
                    // Stream completed without error; nothing left to scan.
                    return;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (RpcException ex) when (ex.InnerException is OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "MWEB scan for a key failed (mwebd unavailable?); retrying in 5s");
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), cancellation);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }
    }
}
