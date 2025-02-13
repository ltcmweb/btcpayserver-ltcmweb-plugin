using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.LitecoinMweb.Configuration;
using BTCPayServer.Plugins.LitecoinMweb.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using GrpcMwebdClient;
using Grpc.Net.Client;

namespace BTCPayServer.Plugins.LitecoinMweb.Services
{
    public class LitecoinMwebListener(
        InvoiceRepository invoiceRepository,
        EventAggregator eventAggregator,
        LitecoinMwebConfiguration configuration,
        LitecoinMwebScanner scanner,
        ILogger<LitecoinMwebListener> logger,
        PaymentMethodHandlerDictionary handlers,
        InvoiceActivator invoiceActivator,
        PaymentService paymentService) : EventHostedServiceBase(eventAggregator, logger)
    {
        public override async Task StartAsync(CancellationToken cancellation)
        {
            await base.StartAsync(cancellation);
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("LTCMWEB");
            var handler = (LitecoinMwebPaymentMethodHandler)handlers[pmi];
            var invoices = await invoiceRepository.GetMonitoredInvoices(pmi, cancellation);
            invoices.Select(invoice => invoice.GetPaymentPrompt(pmi))
                .Select(prompt => handler.ParsePaymentPromptDetails(prompt.Details).ScanKey)
                .ToList().ForEach(scanner.StartScan);
        }

        protected override void SubscribeToEvents()
        {
            base.SubscribeToEvents();
            Subscribe<NewBlockEvent>();
            Subscribe<Utxo>();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellation)
        {
            if (evt is NewBlockEvent blockEvent)
            {
                if (blockEvent.PaymentMethodId == PaymentTypes.CHAIN.GetPaymentMethodId("LTC"))
                {
                    var info = (NBXplorer.Models.NewBlockEvent)blockEvent.AdditionalInfo;
                    var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("LTCMWEB");
                    var invoices = await invoiceRepository.GetMonitoredInvoices(pmi, cancellation);
                    await UpdatePaymentStates(invoices, info.Height);
                }
            }
            else if (evt is Utxo utxo)
            {
                await OnUtxo(utxo);
            }
        }

        private async Task ReceivedPayment(InvoiceEntity invoice, PaymentEntity payment)
        {
            logger.LogInformation($"Invoice {invoice.Id} received payment {payment.Value} {payment.Currency} {payment.Id}");

            var prompt = invoice.GetPaymentPrompt(payment.PaymentMethodId);
            if (prompt != null && prompt.Activated &&
                prompt.Destination == payment.Destination &&
                prompt.Calculate().Due > 0.0m)
            {
                await invoiceActivator.ActivateInvoicePaymentMethod(invoice.Id, payment.PaymentMethodId, true);
                invoice = await invoiceRepository.GetInvoice(invoice.Id);
            }

            EventAggregator.Publish(new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) { Payment = payment });
        }

        private async Task UpdatePaymentStates(InvoiceEntity[] invoices, int height)
        {
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("LTCMWEB");
            var handler = (LitecoinMwebPaymentMethodHandler)handlers[pmi];
            var paymentsToUpdate = new List<(PaymentEntity payment, InvoiceEntity invoice)>();

            foreach (var invoice in invoices)
            {
                foreach (var payment in GetAllLitecoinMwebPayments(invoice))
                {
                    var paymentData = handler.ParsePaymentDetails(payment.Details);
                    if (paymentData.BlockHeight > 0 && height >= paymentData.BlockHeight)
                    {
                        var confirmations = height - paymentData.BlockHeight + 1;
                        await HandlePaymentData(payment.Value, paymentData.OutputId, confirmations,
                            paymentData.BlockHeight, invoice, paymentsToUpdate);
                    }
                }
            }

            await paymentService.UpdatePayments([.. paymentsToUpdate.Select(tuple => tuple.payment)]);
            foreach (var valueTuples in paymentsToUpdate.GroupBy(entity => entity.invoice))
            {
                EventAggregator.Publish(new InvoiceNeedUpdateEvent(valueTuples.Key.Id));
            }
        }

        private async Task OnUtxo(Utxo utxo)
        {
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("LTCMWEB");
            var handler = (LitecoinMwebPaymentMethodHandler)handlers[pmi];
            var paymentsToUpdate = new List<(PaymentEntity payment, InvoiceEntity invoice)>();

            using var channel = GrpcChannel.ForAddress(configuration.DaemonRpcUri);
            var client = new Rpc.RpcClient(channel);
            var status = await client.StatusAsync(new StatusRequest());
            var confirmations = utxo.Height > 0 ? status.BlockHeaderHeight - utxo.Height + 1 : 0;

            // Find the invoice corresponding to this address, else skip
            var invoice = (await invoiceRepository.GetMonitoredInvoices(pmi))
                .Select(invoice => invoice.GetPaymentPrompt(pmi))
                .Where(prompt => handler.ParsePaymentPromptDetails(prompt.Details).Address == utxo.Address)
                .Select(prompt => prompt.ParentEntity).FirstOrDefault();
            if (invoice != null)
            {
                var amt = utxo.Value.ToString(CultureInfo.InvariantCulture).PadLeft(8, '0');
                amt = amt.Length == 8 ? $"0.{amt}" : amt.Insert(amt.Length - 8, ".");
                var amount = decimal.Parse(amt, CultureInfo.InvariantCulture);
                await HandlePaymentData(amount, utxo.OutputId, confirmations, utxo.Height, invoice, paymentsToUpdate);
            }

            await paymentService.UpdatePayments([.. paymentsToUpdate.Select(tuple => tuple.payment)]);
            foreach (var valueTuples in paymentsToUpdate.GroupBy(entity => entity.invoice))
            {
                EventAggregator.Publish(new InvoiceNeedUpdateEvent(valueTuples.Key.Id));
            }
        }

        private async Task HandlePaymentData(decimal amount, string outputId,
            long confirmations, long blockHeight, InvoiceEntity invoice,
            List<(PaymentEntity payment, InvoiceEntity invoice)> paymentsToUpdate)
        {
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("LTCMWEB");
            var handler = (LitecoinMwebPaymentMethodHandler)handlers[pmi];
            var promptDetails = handler.ParsePaymentPromptDetails(invoice.GetPaymentPrompt(pmi).Details);
            if (blockHeight > 0 && blockHeight < promptDetails.FromHeight) return;

            var details = new LitecoinMwebPaymentData
            {
                OutputId = outputId,
                ConfirmationCount = confirmations,
                BlockHeight = blockHeight,
                InvoiceSettledConfirmationThreshold = promptDetails.InvoiceSettledConfirmationThreshold
            };
            var status = GetStatus(details, invoice.SpeedPolicy) ? PaymentStatus.Settled : PaymentStatus.Processing;
            var paymentData = new PaymentData
            {
                Status = status,
                Amount = amount,
                Created = DateTimeOffset.UtcNow,
                Id = outputId,
                Currency = "LTC",
                InvoiceDataId = invoice.Id,
            }.Set(invoice, handler, details);

            // Check if this output exists as a payment to this invoice already
            var alreadyExistingPaymentThatMatches = GetAllLitecoinMwebPayments(invoice)
                .SingleOrDefault(c => c.Id == paymentData.Id && c.PaymentMethodId == pmi);

            // If it doesnt, add it and assign a new address to the system if a balance is still due
            if (alreadyExistingPaymentThatMatches == null)
            {
                var payment = await paymentService.AddPayment(paymentData, [outputId]);
                if (payment != null) await ReceivedPayment(invoice, payment);
            }
            else
            {
                // Else update it with the new data
                alreadyExistingPaymentThatMatches.Status = status;
                alreadyExistingPaymentThatMatches.Details = JToken.FromObject(details, handler.Serializer);
                paymentsToUpdate.Add((alreadyExistingPaymentThatMatches, invoice));
            }
        }

        private static bool GetStatus(LitecoinMwebPaymentData details, SpeedPolicy speedPolicy)
            => ConfirmationsRequired(details, speedPolicy) <= details.ConfirmationCount;

        public static long ConfirmationsRequired(LitecoinMwebPaymentData details, SpeedPolicy speedPolicy)
            => (details, speedPolicy) switch
            {
                ({ InvoiceSettledConfirmationThreshold: long v }, _) => v,
                (_, SpeedPolicy.HighSpeed) => 0,
                (_, SpeedPolicy.MediumSpeed) => 1,
                (_, SpeedPolicy.LowMediumSpeed) => 2,
                (_, SpeedPolicy.LowSpeed) => 6,
                _ => 6,
            };

        private static IEnumerable<PaymentEntity> GetAllLitecoinMwebPayments(InvoiceEntity invoice)
        {
            return invoice.GetPayments(false).Where(p =>
                p.PaymentMethodId == PaymentTypes.CHAIN.GetPaymentMethodId("LTCMWEB"));
        }
    }
}
