using BTCPayServer.Plugins.LitecoinMweb.Configuration;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcMwebdClient;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Plugins.LitecoinMweb.Services
{
    public class LitecoinMwebScanner(
        EventAggregator eventAggregator,
        LitecoinMwebConfiguration configuration) : IHostedService
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
                catch (OperationCanceledException) { }
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
            }
            catch (RpcException ex) when (ex.InnerException is OperationCanceledException)
            {
                throw ex.InnerException;
            }
        }
    }
}
