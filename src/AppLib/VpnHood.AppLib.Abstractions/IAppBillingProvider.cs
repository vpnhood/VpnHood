using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IAppBillingProvider : IDisposable
{
    string ProviderName { get; }

    /// <summary>
    /// The store's own manage-subscriptions page for the signed-in store account (cancel, change
    /// plan, payment method). Self-declared by the store provider so no UI ever hardcodes a store
    /// URL or names another platform's store; surfaced to the SPA through AppFeatures. Null when
    /// the store has no such page.
    /// </summary>
    Uri? SubscriptionManagementUrl { get; }

    /// <summary>
    /// Prices the given products. The store is not asked WHICH products exist — it cannot answer that
    /// (neither StoreKit nor Play Billing can list an app's own catalog) and it is not the authority
    /// on it either: the account backend decides what may be sold, this prices and localizes it.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(IReadOnlyList<string> productIds,
        CancellationToken cancellationToken);

    Task<AppPurchaseResult> Purchase(IUiContext uiContext, PurchaseParams purchaseParams,
        CancellationToken cancellationToken);

    /// <summary>Restore previously purchased items from the store. Null when there is nothing to restore.</summary>
    Task<AppPurchaseResult?> RestorePurchase(IUiContext uiContext, CancellationToken cancellationToken);

    BillingPurchaseState PurchaseState { get; }
}
