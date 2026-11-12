using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;

namespace VpnHood.AppLib.Abstractions.Billing;

/// <summary>
/// The calls that take the window get null where none is open, as in the desktop service: a store
/// whose sheet needs one fails then, and the web store, whose checkout the UI opens, needs none.
/// </summary>
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
    /// <param name="uiContext">The window, or <see langword="null"/> where none is open.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the store's surface is shown.</returns>
    /// <exception cref="NotSupportedException">The store has no such surface here, as the web store's.</exception>
    /// <exception cref="VpnHood.Core.Common.Exceptions.UiContextNotAvailableException">
    /// <paramref name="uiContext"/> is <see langword="null"/>, and the store needs a window.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task OpenSubscriptionManagement(IUiContext? uiContext, CancellationToken cancellationToken);

    /// <summary>
    /// Prices the given products. The store is not asked WHICH products exist — it cannot answer that
    /// (neither StoreKit nor Play Billing can list an app's own catalog) and it is not the authority
    /// on it either: the account backend decides what may be sold, this prices and localizes it.
    /// </summary>
    /// <param name="productIds">The product IDs the account backend gives (<see cref="Accounts.IAccountProvider.GetProductIds" />).</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the plans the store prices for them.</returns>
    /// <exception cref="VpnHood.Net.Toolkit.ApiClients.ApiException">
    /// For the web store, the account backend refused the request.
    /// </exception>
    /// <exception cref="BillingException">The store failed; its <see cref="BillingException.Code"/> says how.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(IReadOnlyList<string> productIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs the store's payment flow and answers its proof of purchase.
    /// <para>
    /// Two arguments because they have two owners: <paramref name="purchaseParams" /> is the buyer's
    /// choice, <paramref name="attribution" /> is the app's answer to whose account is paying.
    /// </para>
    /// </summary>
    /// <param name="uiContext">The window, or <see langword="null"/> where none is open.</param>
    /// <param name="purchaseParams">The plan the buyer chose.</param>
    /// <param name="attribution">Whose account the purchase is for.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the store's proof of purchase.</returns>
    /// <exception cref="BillingException">
    /// The store failed, or the person closed its payment flow (<see cref="BillingErrorCode.Cancelled" />); its
    /// <see cref="BillingException.Code"/> says how.
    /// </exception>
    /// <exception cref="NotSupportedException">The store sells outside the app, as the web store, on its checkout page.</exception>
    /// <exception cref="InvalidOperationException">
    /// A purchase is under way already, or the account's ID does not fit the store, as the App Store's, which takes a
    /// UUID.
    /// </exception>
    /// <exception cref="ArgumentException">The plan names a product the store does not have.</exception>
    /// <exception cref="VpnHood.Core.Common.Exceptions.UiContextNotAvailableException">
    /// <paramref name="uiContext"/> is <see langword="null"/>, and the store needs a window.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<PurchaseProof> Purchase(IUiContext? uiContext, PurchaseParams purchaseParams,
        PurchaseAttribution attribution, CancellationToken cancellationToken);

    /// <summary>
    /// The proof for a subscription this store account already owns, or null when it owns none.
    /// Silent: it reads what the device already knows and never prompts.
    /// </summary>
    /// <param name="uiContext">The window, or <see langword="null"/> where none is open.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result is the proof, or <see langword="null"/> where the store account owns no subscription.
    /// </returns>
    /// <exception cref="BillingException">The store failed; its <see cref="BillingException.Code"/> says how.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<PurchaseProof?> RestorePurchase(IUiContext? uiContext, CancellationToken cancellationToken);

    /// <summary>
    /// Tells the store the purchase behind this proof is delivered: the account backend has redeemed
    /// it. Called only then, so a purchase whose redeeming failed stays open where the store keeps one
    /// open (StoreKit's unfinished transaction), and a later Restore presents it again. A store whose
    /// backend closes the purchase itself (Play's acknowledgement) has nothing to do here.
    /// </summary>
    /// <param name="purchaseProof">The proof the account backend has redeemed.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="BillingException">The store failed; its <see cref="BillingException.Code"/> says how.</exception>
    /// <exception cref="ArgumentException">
    /// The proof is not one this provider answered, as the App Store's checks.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task FinishPurchase(PurchaseProof purchaseProof, CancellationToken cancellationToken);

    /// <summary>Gets where the store's payment flow stands.</summary>
    PurchaseState PurchaseState { get; }
}
