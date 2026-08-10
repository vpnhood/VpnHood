using VpnHood.AppLib.Abstractions;
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
public class AppStoreBillingProvider(
    IReadOnlyList<string> productIds,
    IStoreKitBridge? bridge = null)
    : IAppBillingProvider
{
    private readonly IStoreKitBridge _bridge = bridge ?? new NativeStoreKitBridge();

    public string ProviderName => "AppStore";
    public BillingPurchaseState PurchaseState { get; private set; }

    // Apple's system page for every subscription on the Apple ID; there is no per-product deep link.
    public Uri? SubscriptionManagementUrl => new("https://apps.apple.com/account/subscriptions");

    public async Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(CancellationToken cancellationToken)
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

    public async Task<AppPurchaseResult> Purchase(IUiContext uiContext, PurchaseParams purchaseParams,
        CancellationToken cancellationToken)
    {
        // Apple binds the purchase to the account via appAccountToken — the portal's
        // external uid, provided by the order processor's attribution
        var appAccountToken = purchaseParams.Attribution?.AppAccountToken
            ?? throw new InvalidOperationException(
                "The purchase has no appAccountToken attribution. Sign in before purchasing.");

        PurchaseState = BillingPurchaseState.Started;
        try {
            var purchase = await _bridge
                .Purchase(purchaseParams.PurchaseToken, appAccountToken, cancellationToken).Vhc();

            return purchase.State switch {
                StoreKitPurchase.StatePurchased => new AppPurchaseResult {
                    ProviderOrderId = purchase.TransactionId
                        ?? throw new InvalidOperationException("StoreKit returned no transaction id."),
                    PurchaseData = purchase.Jws
                },
                StoreKitPurchase.StateCancelled => throw new OperationCanceledException("The purchase was cancelled."),
                _ => throw new InvalidOperationException(
                    "The purchase is awaiting approval (Ask to Buy). It will be delivered once approved.")
            };
        }
        finally {
            PurchaseState = BillingPurchaseState.None;
        }
    }

    /// <summary>Apple review requirement: surface previously purchased items without a new charge.</summary>
    public async Task<AppPurchaseResult?> RestorePurchase(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var entitlement = await _bridge.CurrentEntitlement(cancellationToken).Vhc();
        if (entitlement?.TransactionId == null)
            return null;

        return new AppPurchaseResult {
            ProviderOrderId = entitlement.TransactionId,
            PurchaseData = entitlement.Jws
        };
    }

    public void Dispose()
    {
    }
}
