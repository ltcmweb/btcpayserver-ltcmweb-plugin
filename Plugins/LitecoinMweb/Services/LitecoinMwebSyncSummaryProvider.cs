using System.Collections.Generic;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.LitecoinMweb.Services
{
    public class LitecoinMwebSyncSummaryProvider(
        LitecoinMwebSyncSummary syncSummary) : ISyncSummaryProvider
    {
        public bool AllAvailable()
        {
            return syncSummary.Summary?.DaemonAvailable ?? false;
        }

        public string Partial { get; } = "/Views/LitecoinMweb/LitecoinMwebSyncSummary.cshtml";

        public IEnumerable<ISyncStatus> GetStatuses()
        {
            return [new LitecoinMwebSyncStatus
            {
                Summary = syncSummary.Summary,
                PaymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId("LTC-MWEB").ToString()
            }];
        }
    }

    public class LitecoinMwebSyncStatus: SyncStatus, ISyncStatus
    {
        public override bool Available
        {
            get
            {
                return Summary?.DaemonAvailable ?? false;
            }
        }

        public LitecoinMwebSyncSummary.SyncSummary Summary { get; set; }
    }
}
