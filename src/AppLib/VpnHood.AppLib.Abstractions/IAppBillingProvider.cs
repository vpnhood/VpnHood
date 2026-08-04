using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IAppBillingProvider : IDisposable
{
    string ProviderName { get; }
    Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(CancellationToken cancellationToken);

    Task<AppPurchaseResult> Purchase(IUiContext uiContext, PurchaseParams purchaseParams,
        CancellationToken cancellationToken);

    /// <summary>Restore previously purchased items from the store. Null when there is nothing to restore.</summary>
    Task<AppPurchaseResult?> RestorePurchase(IUiContext uiContext, CancellationToken cancellationToken);

    BillingPurchaseState PurchaseState { get; }
}
