using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.LitecoinMweb.Payments;
using BTCPayServer.Plugins.LitecoinMweb.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.LitecoinMweb.Controllers
{
    [Route("stores/{storeId}/LTC-MWEB")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class UILitecoinMwebStoreController(
        StoreRepository storeRepository,
        LitecoinMwebSyncSummary syncSummary,
        PaymentMethodHandlerDictionary handlers) : Controller
    {
        public StoreData StoreData => HttpContext.GetStoreData();

        private LitecoinMwebPaymentMethodViewModel GetLitecoinMwebPaymentMethodViewModel(StoreData storeData)
        {
            var pmi = new PaymentMethodId("LTC-MWEB");
            var vm = new LitecoinMwebPaymentMethodViewModel
            {
                Summary = syncSummary.Summary,
                SettlementConfirmationThresholdChoice = LitecoinMwebSettlementThresholdChoice.StoreSpeedPolicy,
            };

            if (storeData.GetPaymentMethodConfigs(handlers).TryGetValue(pmi, out var config) &&
                config is LitecoinMwebPaymentPromptDetails settings)
            {
                vm.ViewKeys = settings.ViewKeys;
                if (settings.InvoiceSettledConfirmationThreshold is { } confirmations)
                {
                    vm.SettlementConfirmationThresholdChoice = confirmations switch
                    {
                        0 => LitecoinMwebSettlementThresholdChoice.ZeroConfirmation,
                        1 => LitecoinMwebSettlementThresholdChoice.AtLeastOne,
                        10 => LitecoinMwebSettlementThresholdChoice.AtLeastTen,
                        _ => LitecoinMwebSettlementThresholdChoice.Custom
                    };
                }
                if (vm.SettlementConfirmationThresholdChoice is LitecoinMwebSettlementThresholdChoice.Custom)
                {
                    vm.CustomSettlementConfirmationThreshold = settings.InvoiceSettledConfirmationThreshold;
                }
            }

            return vm;
        }

        [HttpGet]
        public IActionResult GetStoreLitecoinMwebPaymentMethod()
        {
            var vm = GetLitecoinMwebPaymentMethodViewModel(StoreData);
            return View("/Views/LitecoinMweb/GetStoreLitecoinMwebPaymentMethod.cshtml", vm);
        }

        [HttpPost]
        public async Task<IActionResult> GetStoreLitecoinMwebPaymentMethod(LitecoinMwebPaymentMethodViewModel viewModel)
        {
            if (!ModelState.IsValid)
            {
                var vm = GetLitecoinMwebPaymentMethodViewModel(StoreData);
                vm.ViewKeys = viewModel.ViewKeys;
                vm.SettlementConfirmationThresholdChoice = viewModel.SettlementConfirmationThresholdChoice;
                vm.CustomSettlementConfirmationThreshold = viewModel.CustomSettlementConfirmationThreshold;
                return View("/Views/LitecoinMweb/GetStoreLitecoinMwebPaymentMethod.cshtml", vm);
            }

            var storeData = StoreData;
            var blob = storeData.GetStoreBlob();
            var pmi = new PaymentMethodId("LTC-MWEB");
            storeData.SetPaymentMethodConfig(handlers[pmi], new LitecoinMwebPaymentPromptDetails
            {
                ViewKeys = viewModel.ViewKeys,
                InvoiceSettledConfirmationThreshold = viewModel.SettlementConfirmationThresholdChoice switch
                {
                    LitecoinMwebSettlementThresholdChoice.ZeroConfirmation => 0,
                    LitecoinMwebSettlementThresholdChoice.AtLeastOne => 1,
                    LitecoinMwebSettlementThresholdChoice.AtLeastTen => 10,
                    LitecoinMwebSettlementThresholdChoice.Custom when viewModel.CustomSettlementConfirmationThreshold is { } custom => custom,
                    _ => null
                }
            });

            storeData.SetStoreBlob(blob);
            await storeRepository.UpdateStore(storeData);

            TempData[WellKnownTempData.SuccessMessage] = "Litecoin MWEB settings updated successfully";
            return RedirectToAction("GetStoreLitecoinMwebPaymentMethod", new { storeId = StoreData.Id });
        }

        public class LitecoinMwebPaymentMethodViewModel : IValidatableObject
        {
            public LitecoinMwebSyncSummary.SyncSummary Summary { get; set; }

            [Display(Name = "MWEB View Keys"), Required]
            [RegularExpression(@"^\s*[a-fA-F0-9]{64}\s+[a-fA-F0-9]{66}\s*$",
                ErrorMessage = "The field must contain a 32-byte hex string followed by a 33-byte hex string, separated by whitespace.")]
            public string ViewKeys { get; set; }
            [Display(Name = "Consider the invoice settled when the payment transaction …")]
            public LitecoinMwebSettlementThresholdChoice SettlementConfirmationThresholdChoice { get; set; }
            [Display(Name = "Required Confirmations"), Range(0, 100)]
            public long? CustomSettlementConfirmationThreshold { get; set; }

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                if (SettlementConfirmationThresholdChoice is LitecoinMwebSettlementThresholdChoice.Custom
                    && CustomSettlementConfirmationThreshold is null)
                {
                    yield return new ValidationResult(
                        "You must specify the number of required confirmations when using a custom threshold.",
                        [nameof(CustomSettlementConfirmationThreshold)]);
                }
            }
        }

        public enum LitecoinMwebSettlementThresholdChoice
        {
            [Display(Name = "Store Speed Policy", Description = "Use the store's speed policy")]
            StoreSpeedPolicy,
            [Display(Name = "Zero Confirmation", Description = "Is unconfirmed")]
            ZeroConfirmation,
            [Display(Name = "At Least One", Description = "Has at least 1 confirmation")]
            AtLeastOne,
            [Display(Name = "At Least Ten", Description = "Has at least 10 confirmations")]
            AtLeastTen,
            [Display(Name = "Custom", Description = "Custom")]
            Custom
        }
    }
}
