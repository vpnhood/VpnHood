using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Abstractions.Billing;

public interface IBillingProvider : IDisposable
{
    /// <summary>
    /// Which store this is (see <see cref="StoreIds" />), in the vocabulary the account backend
    /// speaks. Declared here because the provider is the one thing that cannot be wrong about it —
    /// so nothing that wires a store has to state it a second time and risk disagreeing.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Whether this device can show the store's own manage-subscriptions surface (cancel, change
    /// plan, payment method). A runtime question, not a constant: a TV carries a store that cannot
    /// show that screen, and answering false there is what lets the UI say "manage it where you
    /// bought it" instead of offering a control that does nothing.
    /// </summary>
    bool IsSubscriptionManagementSupported { get; }

    /// <summary>
    /// Shows that surface. The store does it its own way — an in-app sheet, its own app — so no URL
    /// crosses to the UI and no browser has to exist. Only ever called for a subscription THIS store
    /// billed, and only while <see cref="IsSubscriptionManagementSupported" />: the app refuses the
    /// call otherwise, so an implementation need not re-check its own answer.
    /// </summary>
    Task OpenSubscriptionManagement(IUiContext uiContext, CancellationToken cancellationToken);

    /// <summary>
    /// Prices the given products. The store is not asked WHICH products exist — it cannot answer that
    /// (neither StoreKit nor Play Billing can list an app's own catalog) and it is not the authority
    /// on it either: the account backend decides what may be sold, this prices and localizes it.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(IReadOnlyList<string> productIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs the store's payment flow and answers its proof of purchase.
    /// <para>
    /// Two arguments because they have two owners: <paramref name="purchaseParams" /> is the buyer's
    /// choice, <paramref name="attribution" /> is the app's answer to whose account is paying.
    /// </para>
    /// </summary>
    Task<PurchaseProof> Purchase(IUiContext uiContext, PurchaseParams purchaseParams,
        PurchaseAttribution attribution, CancellationToken cancellationToken);

    /// <summary>
    /// The proof for a subscription this store account already owns, or null when it owns none.
    /// Silent: it reads what the device already knows and never prompts.
    /// </summary>
    Task<PurchaseProof?> RestorePurchase(IUiContext uiContext, CancellationToken cancellationToken);

    PurchaseState PurchaseState { get; }
}
