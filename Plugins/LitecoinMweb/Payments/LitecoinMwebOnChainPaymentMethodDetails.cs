namespace BTCPayServer.Plugins.LitecoinMweb.Payments
{
    public class LitecoinMwebOnChainPaymentMethodDetails
    {
        public int FromHeight { get; set; }
        public string Address { get; set; }
        public string ScanKey { get; set; }
        public string SpendPubKey { get; set; }
        public uint AddressIndex { get; set; }
        public long? InvoiceSettledConfirmationThreshold { get; set; }
    }
}
