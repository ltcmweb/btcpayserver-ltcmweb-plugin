using System;
using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.LitecoinMweb.ViewModels
{
    public class LitecoinMwebPaymentViewModel
    {
        public PaymentMethodId PaymentMethodId { get; set; }
        public string Confirmations { get; set; }
        public string DepositAddress { get; set; }
        public string Amount { get; set; }
        public string OutputId { get; set; }
        public DateTimeOffset ReceivedTime { get; set; }
        public string TransactionLink { get; set; }
        public string Currency { get; set; }
    }
}
