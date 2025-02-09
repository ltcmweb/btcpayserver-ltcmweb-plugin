namespace BTCPayServer.Plugins.LitecoinMweb.Payments
{
    public class LitecoinMwebPaymentData
    {
        public long BlockHeight { get; set; }
        public long ConfirmationCount { get; set; }
        public string OutputId { get; set; }
        public long? InvoiceSettledConfirmationThreshold { get; set; }
    }
}
