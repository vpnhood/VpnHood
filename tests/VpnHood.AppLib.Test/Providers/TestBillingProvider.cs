using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Test.Providers;

internal class TestBillingProvider : IBillingProvider
{
    public Exception? PurchaseException { get; set; }
    public Exception? SubscriptionPlanException { get; set; }
    public PurchaseProof? RestoreResult { get; set; }
    public PurchaseParams? LastPurchaseParams { get; private set; }
    public PurchaseAttribution? LastAttribution { get; private set; }

    public string ProviderId { get; set; } = StoreIds.GooglePlay;
    public bool IsSubscriptionManagementSupported { get; set; } = true;
    public bool WasSubscriptionManagementOpened { get; private set; }

    public Task OpenSubscriptionManagement(IUiContext uiContext, CancellationToken cancellationToken)
    {
        WasSubscriptionManagementOpened = true;
        return Task.CompletedTask;
    }

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

    public async Task<PurchaseProof> Purchase(IUiContext uiContext, PurchaseParams purchaseParams,
        PurchaseAttribution attribution, CancellationToken cancellationToken)
    {
        LastPurchaseParams = purchaseParams;
        LastAttribution = attribution;
        if (PurchaseException != null)
            throw PurchaseException;

        await Task.CompletedTask;
        return new PurchaseProof { Value = "test_purchase_data" };
    }

    public Task<PurchaseProof?> RestorePurchase(IUiContext uiContext, CancellationToken cancellationToken)
    {
        return Task.FromResult(RestoreResult);
    }

    public PurchaseState PurchaseState => PurchaseState.None;

    public void Dispose()
    {
    }
}
