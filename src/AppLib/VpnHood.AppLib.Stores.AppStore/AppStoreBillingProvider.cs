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
public class AppStoreBillingProvider(IStoreKitBridge? bridge = null)
    : IBillingProvider
{
    private readonly IStoreKitBridge _bridge = bridge ?? new NativeStoreKitBridge();

    public PurchaseState PurchaseState { get; private set; }
    public string ProviderId => StoreIds.AppStore;

    // StoreKit presents Apple's own sheet inside the app, so nothing here opens a URL and the user
    // never leaves. Available since iOS 15, which is this project's minimum — no fallback path, and
    // no store address anywhere in the codebase.
    public bool IsSubscriptionManagementSupported => true;

    public Task OpenSubscriptionManagement(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        // Apple's sheet shows over the window
        if (uiContext is null)
            throw new UiContextNotAvailableException();

        return _bridge.ShowManageSubscriptions(cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(IReadOnlyList<string> productIds,
        CancellationToken cancellationToken)
    {
        var products = await CallBridge(() => _bridge.LoadProducts(productIds, cancellationToken), cancellationToken).Vhc();

        // subscriptions are all this app sells; StoreKit gives subscription terms to no other type
        // ReSharper disable once UseCollectionExpression
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

    /// <summary>Apple review requirement: surface previously purchased items without a new charge.</summary>
    public async Task<PurchaseProof?> RestorePurchase(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        var entitlements = await CallBridge(() => _bridge.CurrentEntitlements(cancellationToken), cancellationToken).Vhc();

        // one subscription serves an account, so the newest purchase is the one to present
        var newest = entitlements.MaxBy(entitlement => entitlement.PurchaseDate);
        return newest == null ? null : ToPurchaseProof(newest);
    }

    // StoreKit keeps a transaction unfinished until the app has delivered it, which here is the
    // portal redeeming its JWS. A restored one was finished long ago, and finishing it again
    // changes nothing.
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

    public void Dispose()
    {
    }
}
