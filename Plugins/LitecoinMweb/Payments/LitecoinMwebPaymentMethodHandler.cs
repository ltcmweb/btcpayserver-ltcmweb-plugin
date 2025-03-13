using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.LitecoinMweb.Configuration;
using BTCPayServer.Plugins.LitecoinMweb.Services;
using BTCPayServer.Services.Invoices;
using Google.Protobuf;
using Grpc.Net.Client;
using GrpcMwebdClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.LitecoinMweb.Payments
{
    public class LitecoinMwebPaymentMethodHandler(
        LitecoinMwebSpecificBtcPayNetwork network,
        LitecoinMwebConfiguration configuration,
        LitecoinMwebScanner scanner,
        InvoiceRepository invoiceRepository) : IPaymentMethodHandler
    {
        public LitecoinMwebSpecificBtcPayNetwork Network => network;
        public JsonSerializer Serializer { get; } = BlobSerializer.CreateSerializer().Serializer;

        public PaymentMethodId PaymentMethodId { get; } = new PaymentMethodId(network.CryptoCode);

        public Task BeforeFetchingRates(PaymentMethodContext context)
        {
            context.Prompt.Currency = "LTC";
            context.Prompt.Divisibility = network.Divisibility;

            if (context.Prompt.Activated)
            {
                var config = ParsePaymentMethodConfig(context.PaymentMethodConfig);
                var keys = config.ViewKeys
                    .Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(Convert.FromHexString)
                    .Select(Convert.ToHexString).ToArray();

                var prepare = new Prepare
                {
                    ScanKey = keys[0],
                    SpendPubKey = keys[1],
                };
                context.State = prepare;

                if (context.InvoiceEntity.GetPaymentPrompt(PaymentMethodId) is { } prompt)
                {
                    var details = ParsePaymentPromptDetails(prompt.Details);
                    if (details.ScanKey == prepare.ScanKey && details.SpendPubKey == prepare.SpendPubKey)
                    {
                        prepare.ReserveAddress = Task.FromResult(new ReserveAddressResult
                        {
                            FromHeight = details.FromHeight,
                            Address = details.Address,
                            AddressIndex = details.AddressIndex,
                        });
                    }
                }
                else
                {
                    prepare.ReserveAddress = ReserveAddress(prepare.ScanKey, prepare.SpendPubKey);
                }
            }

            return Task.CompletedTask;
        }

        public async Task ConfigurePrompt(PaymentMethodContext context)
        {
            var config = ParsePaymentMethodConfig(context.PaymentMethodConfig);
            var prepare = (Prepare)context.State;
            var result = await prepare.ReserveAddress;

            var details = new LitecoinMwebOnChainPaymentMethodDetails
            {
                FromHeight = result.FromHeight,
                Address = result.Address,
                ScanKey = prepare.ScanKey,
                SpendPubKey = prepare.SpendPubKey,
                AddressIndex = result.AddressIndex,
                InvoiceSettledConfirmationThreshold = config.InvoiceSettledConfirmationThreshold
            };

            context.Prompt.Destination = details.Address;
            context.Prompt.Details = JObject.FromObject(details, Serializer);

            scanner.StartScan(prepare.ScanKey, result.FromHeight);
        }

        private readonly Dictionary<(string, string), HashSet<uint>> reservedAddressIndices = [];

        private async Task<ReserveAddressResult> ReserveAddress(string scanKey, string spendPubKey)
        {
            var invoices = await invoiceRepository.GetMonitoredInvoices(PaymentMethodId);
            var addressIndicesInUse = invoices
                .Select(invoice => invoice.GetPaymentPrompt(PaymentMethodId))
                .Select(prompt => ParsePaymentPromptDetails(prompt.Details))
                .Where(details => details.ScanKey == scanKey && details.SpendPubKey == spendPubKey)
                .Select(details => details.AddressIndex).ToHashSet();

            uint addressIndex = 1;
            lock (reservedAddressIndices)
            {
                var indicesInUse = reservedAddressIndices.GetValueOrDefault((scanKey, spendPubKey), []);
                reservedAddressIndices[(scanKey, spendPubKey)] = indicesInUse;
                while (addressIndicesInUse.Contains(addressIndex) ||
                       indicesInUse.Contains(addressIndex)) addressIndex++;
                indicesInUse.Add(addressIndex);
            }

            using var channel = GrpcChannel.ForAddress(configuration.DaemonRpcUri);
            var client = new Rpc.RpcClient(channel);
            var response = await client.AddressesAsync(new AddressRequest
            {
                FromIndex = addressIndex,
                ToIndex = addressIndex + 1,
                ScanSecret = ByteString.CopyFrom(Convert.FromHexString(scanKey)),
                SpendPubkey = ByteString.CopyFrom(Convert.FromHexString(spendPubKey))
            });

            var status = await client.StatusAsync(new StatusRequest());

            return new ReserveAddressResult
            {
                FromHeight = status.BlockHeaderHeight + 1,
                Address = response.Address[0],
                AddressIndex = addressIndex,
            };
        }

        public Task AfterSavingInvoice(PaymentMethodContext context)
        {
            if (context.Prompt.Details != null) lock (reservedAddressIndices)
            {
                var details = ParsePaymentPromptDetails(context.Prompt.Details);
                var indicesInUse = reservedAddressIndices.GetValueOrDefault((details.ScanKey, details.SpendPubKey));
                indicesInUse?.Remove(details.AddressIndex);
            }

            return Task.CompletedTask;
        }

        private LitecoinMwebPaymentPromptDetails ParsePaymentMethodConfig(JToken config)
        {
            return config.ToObject<LitecoinMwebPaymentPromptDetails>(Serializer) ?? throw new FormatException($"Invalid {nameof(LitecoinMwebPaymentMethodHandler)}");
        }
        object IPaymentMethodHandler.ParsePaymentMethodConfig(JToken config)
        {
            return ParsePaymentMethodConfig(config);
        }

        class Prepare
        {
            public string ScanKey { get; internal set; }
            public string SpendPubKey { get; internal set; }
            public Task<ReserveAddressResult> ReserveAddress { get; internal set; }
        }

        class ReserveAddressResult
        {
            public int FromHeight { get; internal set; }
            public string Address { get; internal set; }
            public uint AddressIndex { get; internal set; }
        }

        public LitecoinMwebOnChainPaymentMethodDetails ParsePaymentPromptDetails(JToken details)
        {
            return details.ToObject<LitecoinMwebOnChainPaymentMethodDetails>(Serializer);
        }
        object IPaymentMethodHandler.ParsePaymentPromptDetails(JToken details)
        {
            return ParsePaymentPromptDetails(details);
        }

        public LitecoinMwebPaymentData ParsePaymentDetails(JToken details)
        {
            return details.ToObject<LitecoinMwebPaymentData>(Serializer) ?? throw new FormatException($"Invalid {nameof(LitecoinMwebPaymentMethodHandler)}");
        }
        object IPaymentMethodHandler.ParsePaymentDetails(JToken details)
        {
            return ParsePaymentDetails(details);
        }
    }
}
