using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Test.Providers;

internal class TestBillingProvider : IAppBillingProvider
{
    public Exception? PurchaseException { get; set; }
    public Exception? SubscriptionPlanException { get; set; }
    public AppPurchaseResult? RestoreResult { get; set; }
    public PurchaseParams? LastPurchaseParams { get; private set; }

    public string ProviderName => "Test";
    public Uri? SubscriptionManagementUrl => new("https://test.local/subscriptions");

    /// <summary>The product ids the catalog asked to be priced, as the store received them.</summary>
    public IReadOnlyList<string>? LastRequestedProductIds { get; private set; }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(IReadOnlyList<string> productIds,
        CancellationToken cancellationToken)
    {
        LastRequestedProductIds = productIds;
        if (SubscriptionPlanException != null)
            throw SubscriptionPlanException;

        await Task.CompletedTask;
        return [
            new SubscriptionPlan {
                BasePrice = 12,
                CurrentPrice = 9,
                Period = "P1M",
                PlanToken = "test_plan_1m",
                CurrencySymbol = "$",
                CurrencyCode = "USD"
            }
        ];
    }

    public async Task<AppPurchaseResult> Purchase(IUiContext uiContext, PurchaseParams purchaseParams,
        CancellationToken cancellationToken)
    {
        LastPurchaseParams = purchaseParams;
        if (PurchaseException != null)
            throw PurchaseException;

        await Task.CompletedTask;
        return new AppPurchaseResult {
            ProviderOrderId = Guid.NewGuid().ToString(),
            PurchaseData = "test_purchase_data"
        };
    }

    public Task<AppPurchaseResult?> RestorePurchase(IUiContext uiContext, CancellationToken cancellationToken)
    {
        return Task.FromResult(RestoreResult);
    }

    public BillingPurchaseState PurchaseState => BillingPurchaseState.None;

    public void Dispose()
    {
    }
}
