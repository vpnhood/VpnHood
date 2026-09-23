using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.AppLib.Ios.AppStore.Exceptions;
using VpnHood.AppLib.Ios.StoreKitNative;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Extensions;

namespace VpnHood.AppLib.Ios.AppStore;

/// <summary>
/// StoreKit 2 billing. The plan token is the App Store product id (Apple has
/// no base plans — the product IS the plan+cycle); the purchase proof is the
/// SK2 signed transaction (JWS), which the portal's POST /billing/purchases treats as
/// a pointer and re-fetches server-to-server.
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

    public Task OpenSubscriptionManagement(IUiContext uiContext, CancellationToken cancellationToken)
    {
        return _bridge.ShowManageSubscriptions(cancellationToken);
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(IReadOnlyList<string> productIds,
        CancellationToken cancellationToken)
    {
        var products = await CallBridge(() => _bridge.LoadProducts(productIds, cancellationToken), cancellationToken).Vhc();
        // ReSharper disable once UseCollectionExpression
        return products
            .Select(product => new SubscriptionPlan {
                PlanToken = product.Id,
                BasePrice = product.Price,
                CurrentPrice = product.CurrentPrice,
                Period = product.PeriodIso,
                TrialPeriod = product.TrialPeriodIso,
                CurrencyCode = product.CurrencyCode,
                CurrencySymbol = product.CurrencySymbol
            })
            .ToArray();
    }

    public async Task<PurchaseProof> Purchase(IUiContext uiContext, PurchaseParams purchaseParams,
        PurchaseAttribution attribution, CancellationToken cancellationToken)
    {
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
                StoreKitPurchase.StatePurchased => new PurchaseProof {
                    Value = purchase.Jws
                            ?? throw new InvalidOperationException("StoreKit returned no signed transaction.")
                },
                StoreKitPurchase.StateCancelled => throw AppStoreBillingErrors.Cancelled(),
                _ => throw AppStoreBillingErrors.Pending()
            };
        }
        finally {
            PurchaseState = PurchaseState.None;
        }
    }

    /// <summary>Apple review requirement: surface previously purchased items without a new charge.</summary>
    public async Task<PurchaseProof?> RestorePurchase(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var entitlement = await CallBridge(() => _bridge.CurrentEntitlement(cancellationToken), cancellationToken).Vhc();
        var jws = entitlement?.Jws;
        return jws == null ? null : new PurchaseProof { Value = jws };
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
