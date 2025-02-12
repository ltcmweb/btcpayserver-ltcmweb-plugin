#nullable enable
using System.Globalization;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.LitecoinMweb.Payments
{
    public class LitecoinMwebPaymentLinkExtension(
        PaymentMethodId paymentMethodId,
        LitecoinMwebSpecificBtcPayNetwork network) : IPaymentLinkExtension
    {
        public PaymentMethodId PaymentMethodId { get; } = paymentMethodId;

        public string? GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper)
        {
            var due = prompt.Calculate().Due;
            return $"{network.UriScheme}:{prompt.Destination}?amount={due.ToString(CultureInfo.InvariantCulture)}";
        }
    }
}
