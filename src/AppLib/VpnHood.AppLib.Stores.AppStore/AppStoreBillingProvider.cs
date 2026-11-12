using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.AppLib.Stores.AppStore.Exceptions;
using VpnHood.AppLib.Stores.AppStore.Native;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppLib.Stores.AppStore;

/// <summary>
/// StoreKit 2 billing. The plan token is the App Store product id (Apple has
/// no base plans — the product IS the plan+cycle); the purchase proof is the
/// SK2 signed transaction (JWS), which the portal's POST /billing/purchases treats as
/// a pointer and re-fetches server-to-server. The bridge mirrors StoreKit as it is, so
/// this app's own rules live here: only subscriptions are sold, an introductory offer
/// shapes the first price, the newest entitlement is the one restored, and a purchase
/// is finished at StoreKit once the portal has redeemed it.
/// </summary>
/// <param name="bridge">The bridge to StoreKit, or <see langword="null"/> for the native one.</param>
public class AppStoreBillingProvider(IStoreKitBridge? bridge = null)
    : IBillingProvider
{
    private readonly IStoreKitBridge _bridge = bridge ?? new NativeStoreKitBridge();

    /// <inheritdoc/>
    public PurchaseState PurchaseState { get; private set; }

    /// <inheritdoc/>
    /// <remarks>It is <see cref="StoreIds.AppStore"/>.</remarks>
    public string ProviderId => StoreIds.AppStore;

    /// <inheritdoc/>
    /// <remarks>
    /// StoreKit shows Apple's own sheet in the app, so the person never leaves it. tvOS has no such sheet, so it is
    /// <see langword="false"/> there: a TV's subscriptions are managed in its Settings or from another of the person's
    /// devices.
    /// </remarks>
    public bool IsSubscriptionManagementSupported => !OperatingSystem.IsTvOS();

    /// <inheritdoc/>
    /// <remarks>StoreKit's manage-subscriptions sheet shows over the window.</remarks>
    /// <returns>A task that completes once the person has closed the sheet.</returns>
    /// <exception cref="InvalidOperationException">
    /// StoreKit failed to show the sheet; the error comes as StoreKit's, not as a <see cref="BillingException"/>.
    /// </exception>
    [UnsupportedOSPlatform("tvos")]
    public Task OpenSubscriptionManagement(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        // Apple's sheet shows over the window
        if (uiContext is null)
            throw new UiContextNotAvailableException();

        return _bridge.ShowManageSubscriptions(cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Only subscriptions are priced; an introductory offer the person may still take sets the first price, or the free
    /// trial.
    /// </remarks>
    public async Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(IReadOnlyList<string> productIds,
        CancellationToken cancellationToken)
    {
        var products = await CallBridge(() => _bridge.LoadProducts(productIds, cancellationToken), cancellationToken).Vhc();

        // subscriptions are all this app sells; StoreKit gives subscription terms to no other type
        return products
            .Where(product => product.Subscription != null)
            .Select(ToSubscriptionPlan)
            .ToArray();
    }

    // The first price the person actually pays, and any free-trial phase, come from the
    // introductory offer — only while this person may still take it.
    private static SubscriptionPlan ToSubscriptionPlan(StoreKitProduct product)
    {
        var subscription = product.Subscription
                           ?? throw new ArgumentException("The product is not a subscription.", nameof(product));
        var offer = subscription.IsEligibleForIntroOffer ? subscription.IntroductoryOffer : null;
        var trialOffer = offer?.PaymentMode == StoreKitOffer.PaymentModeFreeTrial ? offer : null;
        var paidOffer = trialOffer == null ? offer : null;

        return new SubscriptionPlan {
            PlanToken = product.Id,
            BasePrice = product.Price,
            CurrentPrice = paidOffer?.Price ?? product.Price,
            Period = subscription.PeriodIso,
            TrialPeriod = trialOffer?.PeriodIso,
            CurrencyCode = product.CurrencyCode,
            CurrencySymbol = product.CurrencySymbol
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The plan token is the App Store product ID; the account's ID goes to StoreKit as the purchase's app account token,
    /// which must be a UUID. The proof is StoreKit's signed transaction (JWS). A purchase StoreKit leaves pending, as one
    /// waiting for a parent's approval, throws a <see cref="BillingException"/> with
    /// <see cref="BillingErrorCode.Pending"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The account's ID is not a UUID, or StoreKit answered a purchase without its transaction.
    /// </exception>
    public async Task<PurchaseProof> Purchase(IUiContext? uiContext, PurchaseParams purchaseParams,
        PurchaseAttribution attribution, CancellationToken cancellationToken)
    {
        // the payment sheet shows over the window
        if (uiContext is null)
            throw new UiContextNotAvailableException();

        // Apple binds the purchase to the account via appAccountToken, and accepts only a UUID —
        // the shaping happens here, where that constraint lives. A backend whose account ids are
        // not UUIDs fails with that as the reason, rather than looking like nobody is signed in.
        if (!Guid.TryParse(attribution.UserId, out var appAccountToken))
            throw new InvalidOperationException(
                $"The App Store needs the account id as a UUID. UserId: {attribution.UserId}");

        PurchaseState = PurchaseState.Started;
        try {
            var purchase = await CallBridge(
                () => _bridge.Purchase(purchaseParams.PlanToken, appAccountToken, cancellationToken),
                cancellationToken).Vhc();

            return purchase.State switch {
                StoreKitPurchase.StatePurchased => ToPurchaseProof(purchase.Transaction
                    ?? throw new InvalidOperationException("StoreKit returned no transaction.")),
                StoreKitPurchase.StateCancelled => throw AppStoreBillingErrors.Cancelled(),
                _ => throw AppStoreBillingErrors.Pending()
            };
        }
        finally {
            PurchaseState = PurchaseState.None;
        }
    }

    /// <inheritdoc/>
    /// <remarks>It presents the newest of the store account's current entitlements, without a new charge.</remarks>
    public async Task<PurchaseProof?> RestorePurchase(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        var entitlements = await CallBridge(() => _bridge.CurrentEntitlements(cancellationToken), cancellationToken).Vhc();

        // one subscription serves an account, so the newest purchase is the one to present
        var newest = entitlements.MaxBy(entitlement => entitlement.PurchaseDate);
        return newest == null ? null : ToPurchaseProof(newest);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// StoreKit keeps a transaction unfinished until the app has delivered it, which here is the portal redeeming its
    /// JWS. A restored transaction may be unfinished too; finishing one already finished changes nothing.
    /// </remarks>
    public async Task FinishPurchase(PurchaseProof purchaseProof, CancellationToken cancellationToken)
    {
        if (purchaseProof is not AppStorePurchaseProof appStoreProof)
            throw new ArgumentException("The proof is not one this provider answered.", nameof(purchaseProof));

        var wasUnfinished = await CallBridge(
            () => _bridge.FinishTransaction(appStoreProof.TransactionId, cancellationToken),
            cancellationToken).Vhc();

        VhLogger.Instance.LogInformation(
            "The App Store transaction is finished. TransactionId: {TransactionId}, WasUnfinished: {WasUnfinished}",
            appStoreProof.TransactionId, wasUnfinished);
    }

    private static AppStorePurchaseProof ToPurchaseProof(StoreKitTransaction transaction)
    {
        return new AppStorePurchaseProof { Value = transaction.Jws, TransactionId = transaction.Id };
    }

    // The billing contract: nothing store-specific crosses the client API. The bridge has no
    // error-code channel (StoreKit failures arrive as plain exceptions with a message), so
    // anything it throws — other than this call's own cancellation — leaves here as a
    // BillingException with code Unknown and the store's message attached.
    private static async Task<T> CallBridge<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try {
            return await action().Vhc();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) when (ex is not BillingException) {
            throw AppStoreBillingErrors.Wrap(ex);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}
