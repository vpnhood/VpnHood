using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.AppLib.Ios.AppStore.StoreKitBridge;
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
        var products = await _bridge.LoadProducts(productIds, cancellationToken).Vhc();
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
            var purchase = await _bridge
                .Purchase(purchaseParams.PlanToken, appAccountToken, cancellationToken).Vhc();

            return purchase.State switch {
                StoreKitPurchase.StatePurchased => new PurchaseProof {
                    Value = purchase.Jws
                            ?? throw new InvalidOperationException("StoreKit returned no signed transaction.")
                },
                StoreKitPurchase.StateCancelled => throw new OperationCanceledException("The purchase was cancelled."),
                _ => throw new InvalidOperationException(
                    "The purchase is awaiting approval (Ask to Buy). It will be delivered once approved.")
            };
        }
        finally {
            PurchaseState = PurchaseState.None;
        }
    }

    /// <summary>Apple review requirement: surface previously purchased items without a new charge.</summary>
    public async Task<PurchaseProof?> RestorePurchase(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var entitlement = await _bridge.CurrentEntitlement(cancellationToken).Vhc();
        var jws = entitlement?.Jws;
        return jws == null ? null : new PurchaseProof { Value = jws };
    }

    public void Dispose()
    {
    }
}
