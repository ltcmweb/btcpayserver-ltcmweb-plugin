using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.Logging;
using BTCPayServer.Plugins.LitecoinMweb.Configuration;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcMwebdClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.LitecoinMweb.Services
{
    public class LitecoinMwebSyncSummary(
        LitecoinMwebConfiguration configuration,
        ExplorerClientProvider explorers,
        Logs logs) : IHostedService
    {
        private SyncSummary _summary;
        public SyncSummary Summary => _summary;

        private CancellationTokenSource _cts;
        private Task _task;

        public Task StartAsync(CancellationToken cancellation)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            _task = StartLoop(_cts.Token);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellation)
        {
            _cts?.Cancel();
            try
            {
                await _task.WaitAsync(cancellation);
            }
            catch (OperationCanceledException) { }
        }

        private async Task StartLoop(CancellationToken cancellation)
        {
            logs.PayServer.LogInformation("Starting listening Litecoin MWEB daemon");

            while (!cancellation.IsCancellationRequested)
            {
                await UpdateSummary(cancellation);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellation);
            }
        }

        private async Task UpdateSummary(CancellationToken cancellation)
        {
            var summary = new SyncSummary();
            try
            {
                var targetHeight = (await explorers.GetExplorerClient("LTC").GetStatusAsync(cancellation)).ChainHeight;

                using var channel = GrpcChannel.ForAddress(configuration.DaemonRpcUri);
                var client = new Rpc.RpcClient(channel);
                var status = await client.StatusAsync(new StatusRequest(), cancellationToken: cancellation);

                summary.CurrentHeight = status.BlockHeaderHeight;
                summary.TargetHeight = targetHeight < summary.CurrentHeight ? summary.CurrentHeight : targetHeight;
                summary.Synced = status.BlockHeaderHeight == targetHeight && status.MwebUtxosHeight == status.BlockHeaderHeight;
                summary.UpdatedAt = DateTime.UtcNow;
                summary.DaemonAvailable = true;
            }
            catch (RpcException ex) when (ex.InnerException is OperationCanceledException)
            {
                throw ex.InnerException;
            }
            catch (Exception ex) when (!cancellation.IsCancellationRequested)
            {
                logs.PayServer.LogError(ex, "Unhandled exception in Summary updater");
            }

            _summary = summary;
        }

        public class SyncSummary
        {
            public bool Synced { get; set; }
            public long CurrentHeight { get; set; }
            public long TargetHeight { get; set; }
            public DateTime UpdatedAt { get; set; }
            public bool DaemonAvailable { get; set; }
        }
    }
}
