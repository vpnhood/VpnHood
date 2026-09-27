using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.App.AvaloniaUI.Dev;

// A store's billing without the store: two sample plans, and a purchase that answers after the
// moment a payment sheet would take, charging no one.
internal sealed class DevBillingProvider(string storeId, bool isSubscriptionManagementSupported) : IBillingProvider
{
    public static IReadOnlyList<string> ProductIds { get; } = ["dev_monthly", "dev_yearly"];

    public string ProviderId => storeId;
    public bool IsSubscriptionManagementSupported => isSubscriptionManagementSupported;
    public PurchaseState PurchaseState => PurchaseState.None;

    public Task OpenSubscriptionManagement(IUiContext uiContext, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(IReadOnlyList<string> productIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SubscriptionPlan> plans = [
            new SubscriptionPlan {
                PlanToken = "dev_monthly", Period = "P1M", TrialPeriod = "P7D",
                BasePrice = 4.99, CurrentPrice = 4.99, CurrencySymbol = "$", CurrencyCode = "USD"
            },
            new SubscriptionPlan {
                PlanToken = "dev_yearly", Period = "P1Y",
                BasePrice = 59.88, CurrentPrice = 39.99, CurrencySymbol = "$", CurrencyCode = "USD"
            }
        ];
        return Task.FromResult(plans);
    }

    public async Task<PurchaseProof> Purchase(IUiContext uiContext, PurchaseParams purchaseParams,
        PurchaseAttribution attribution, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken);
        return new PurchaseProof { Value = purchaseParams.PlanToken };
    }

    public Task<PurchaseProof?> RestorePurchase(IUiContext uiContext, CancellationToken cancellationToken)
    {
        return Task.FromResult<PurchaseProof?>(null);
    }

    public void Dispose()
    {
    }
}
