using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using BTCPayServer.Configuration;
using System.Linq;
using System;
using System.Globalization;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.LitecoinMweb.Configuration;
using BTCPayServer.Plugins.LitecoinMweb.Payments;
using BTCPayServer.Plugins.LitecoinMweb.Services;
using BTCPayServer.Services;
using Microsoft.Extensions.Configuration;
using NBXplorer;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.LitecoinMweb;

public class LitecoinMwebPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=2.0.5" }
    ];

    public override void Execute(IServiceCollection services)
    {
        var pluginServices = (PluginServiceCollection)services;
        var prov = pluginServices.BootstrapServices.GetRequiredService<NBXplorerNetworkProvider>();
        var chainName = prov.NetworkType;

        var network = new LitecoinMwebSpecificBtcPayNetwork
        {
            CryptoCode = "LTC-MWEB",
            DisplayName = "Litecoin MWEB",
            Divisibility = 8,
            DefaultRateRules = [ "LTC_X = LTC_BTC * BTC_X", "LTC_BTC = coingecko(LTC_BTC)" ],
            CryptoImagePath = "ltcmweb.svg",
            UriScheme = "litecoin"
        };
        var blockExplorerLink = chainName == ChainName.Mainnet
                    ? "https://live.blockcypher.com/ltc/tx/{0}/"
                    : "http://explorer.litecointools.com/tx/{0}";
        var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
        services.AddDefaultPrettyName(pmi, network.DisplayName);
        services.AddBTCPayNetwork(network).AddTransactionLinkProvider(pmi, new SimpleTransactionLinkProvider(blockExplorerLink));

        services.AddSingleton(ConfigureLitecoinMwebConfiguration);
        services.AddSingleton<LitecoinMwebSyncSummary>();
        services.AddHostedService(provider => provider.GetRequiredService<LitecoinMwebSyncSummary>());
        services.AddHostedService<LitecoinMwebListener>();
        services.AddSingleton<LitecoinMwebScanner>();
        services.AddHostedService(provider => provider.GetRequiredService<LitecoinMwebScanner>());

        services.AddSingleton(provider => (IPaymentMethodHandler)ActivatorUtilities.CreateInstance(provider, typeof(LitecoinMwebPaymentMethodHandler), [network]));
        services.AddSingleton(provider => (IPaymentLinkExtension)ActivatorUtilities.CreateInstance(provider, typeof(LitecoinMwebPaymentLinkExtension), [network, pmi]));
        services.AddSingleton(provider => (ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(LitecoinMwebCheckoutModelExtension), [network, pmi]));

        services.AddUIExtension("store-wallets-nav", "/Views/LitecoinMweb/StoreWalletsNavLitecoinMwebExtension.cshtml");
        services.AddUIExtension("store-invoices-payments", "/Views/LitecoinMweb/ViewLitecoinMwebPaymentData.cshtml");
        services.AddSingleton<ISyncSummaryProvider, LitecoinMwebSyncSummaryProvider>();
    }

    class SimpleTransactionLinkProvider(string blockExplorerLink) : DefaultTransactionLinkProvider(blockExplorerLink)
    {
        public override string GetTransactionLink(string paymentId)
        {
            if (string.IsNullOrEmpty(BlockExplorerLink)) return null;
            return string.Format(CultureInfo.InvariantCulture, BlockExplorerLink, paymentId);
        }
    }

    private static LitecoinMwebConfiguration ConfigureLitecoinMwebConfiguration(IServiceProvider serviceProvider)
    {
        var configuration = serviceProvider.GetService<IConfiguration>();
        var btcPayNetworkProvider = serviceProvider.GetService<BTCPayNetworkProvider>();
        var result = new LitecoinMwebConfiguration();

        var supportedNetworks = btcPayNetworkProvider.GetAll().OfType<LitecoinMwebSpecificBtcPayNetwork>();

        foreach (var network in supportedNetworks)
        {
            var daemonUri = configuration.GetOrDefault<Uri>("ltc_mweb_daemon_uri", null);
            if (daemonUri == null)
            {
                var logger = serviceProvider.GetRequiredService<ILogger<LitecoinMwebPlugin>>();
                var cryptoCode = network.CryptoCode.ToUpperInvariant();
                if (daemonUri is null)
                {
                    logger.LogWarning("BTCPAY_LTC_MWEB_DAEMON_URI is not configured");
                }
                logger.LogWarning($"{cryptoCode} got disabled as it is not fully configured.");
            }
            else
            {
                result.DaemonRpcUri = daemonUri;
            }
        }
        return result;
    }
}
