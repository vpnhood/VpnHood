using System.Text.Json;
using Android.BillingClient.Api;
using Android.Content;
using Android.Gms.Common;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.AppLib.Droid.Common.Utils;
using VpnHood.AppLib.Droid.GooglePlay.Exceptions;
using VpnHood.Core.Client.Devices.Droid;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using PurchaseState = VpnHood.AppLib.Abstractions.Billing.PurchaseState;

namespace VpnHood.AppLib.Droid.GooglePlay;

public class GooglePlayBillingProvider : IBillingProvider
{
    private readonly Lazy<BillingClient> _billingClient;
    private TaskCompletionSource<PurchaseProof>? _taskCompletionSource;
    public PurchaseState PurchaseState { get; private set; }
    public string ProviderId => StoreIds.GooglePlay;

    // Google's documented deep link, and the only mechanism Play offers — the Billing library has no
    // manage-subscriptions call, unlike StoreKit. The account-wide screen is the fallback; the
    // targeted form below opens the app's own subscription directly.
    private const string SubscriptionsUrl = "https://play.google.com/store/account/subscriptions";

    // Play has no native subscriptions screen on a television: it accepts the deep link and forwards
    // it to a browser. So what decides this is whether a browser exists, not whether the device is a
    // TV — a television with one installed reaches the same web page a phone would.
    public bool IsSubscriptionManagementSupported => AndroidBrowserUtils.IsExternalBrowserAvailable();

    public async Task OpenSubscriptionManagement(IUiContext uiContext, CancellationToken cancellationToken)
    {
        var url = await BuildSubscriptionManagementUrl(cancellationToken).Vhc();

        // An implicit view, the way every other outbound link in the app travels: Play holds
        // verified app links for this host and claims it. Naming the package instead would buy
        // determinism at the price of a visibility declaration, and it is not worth that.
        var appUiContext = (AndroidUiContext)uiContext;
        appUiContext.Activity.StartActivity(new Intent(Intent.ActionView, Android.Net.Uri.Parse(url)));
    }

    /// <summary>
    /// The deep link straight to THIS app's subscription when the store owns one, so the user lands
    /// on the thing they asked to manage instead of a list. Naming it needs the owned product id,
    /// which only the store can answer — and failing to get it is not a reason to refuse: the
    /// account-wide screen still manages the subscription, one tap further along.
    /// </summary>
    private async Task<string> BuildSubscriptionManagementUrl(CancellationToken cancellationToken)
    {
        try {
            var packageName = Application.Context.PackageName;
            var productId = (await GetOwnedSubscription(cancellationToken).Vhc())?.Products.FirstOrDefault();
            return productId == null || packageName == null
                ? SubscriptionsUrl
                : $"{SubscriptionsUrl}?sku={Uri.EscapeDataString(productId)}&package={Uri.EscapeDataString(packageName)}";
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex,
                "Could not name the owned subscription; opening the account-wide screen instead.");
            return SubscriptionsUrl;
        }
    }

    public GooglePlayBillingProvider()
    {
        _billingClient = new Lazy<BillingClient>(() => {
            var builder = BillingClient.NewBuilder(Application.Context);
            builder.SetListener(PurchasesUpdatedListener);

            // We don't have the On-Time Purchase in this app, but if EnablePendingPurchases is not implemented,
            // we get the error "Pending purchases for one-time products must be supported."
            return builder.EnablePendingPurchases(
                PendingPurchasesParams.NewBuilder().EnableOneTimeProducts().Build()
            ).Build();
        });
    }

    private void PurchasesUpdatedListener(BillingResult billingResult, IList<Purchase> purchases)
    {
        switch (billingResult.ResponseCode) {
            case BillingResponseCode.Ok:
                var purchasedItem = purchases.FirstOrDefault();
                if (purchasedItem == null) {
                    _taskCompletionSource?.TrySetException(GoogleBillingErrors.Create(billingResult));
                    break;
                }

                // Play sets no order id while a purchase is still pending; the token alone cannot
                // tell the two apart, so the order id stays the pending probe even though only the
                // token travels onward
                if (purchasedItem.OrderId != null)
                    _taskCompletionSource?.TrySetResult(new PurchaseProof { Value = purchasedItem.PurchaseToken });
                else
                    // Based on Google document, orderId is null on pending state.
                    // The pending state must be handled in the UI to let the user know their subscription will be
                    // available when Google accepts payment and changes the purchase state to PURCHASES.
                    _taskCompletionSource?.TrySetException(
                        GoogleBillingErrors.Create(billingResult, purchasedItem.PurchaseState));
                break;

            default:
                _taskCompletionSource?.TrySetException(GoogleBillingErrors.Create(billingResult));
                break;
        }
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(IReadOnlyList<string> productIds,
        CancellationToken cancellationToken)
    {
        var billingClient = await GetSafeBillingClient(cancellationToken).Vhc();

        // Check if the purchase subscription is supported on the user's device
        try {
            var isDeviceSupportSubscription =
                billingClient.IsFeatureSupported(BillingClient.FeatureType.Subscriptions);
            if (isDeviceSupportSubscription.ResponseCode == BillingResponseCode.FeatureNotSupported)
                throw GoogleBillingErrors.Create(isDeviceSupportSubscription);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not check supported feature with google play.");
            throw;
        }

        // Get products list from GooglePlay.
        try {
            var products = await GetProducts(billingClient, productIds);

            // One plan per (product, base plan) so every purchasable period surfaces. Which plans exist,
            // their order and labels are catalog decisions and must not be inferred here; the store only prices them.
            var subscriptionPlans = products
                .SelectMany(product => (product.GetSubscriptionOfferDetails() ?? [])
                    .GroupBy(offer => offer.BasePlanId)
                    .Select(basePlanOffers => BuildSubscriptionPlan(product, basePlanOffers)))
                .OfType<SubscriptionPlan>()
                .ToArray();

            return subscriptionPlans;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not get products from google play.");
            throw;
        }
    }

    private static SubscriptionPlan? BuildSubscriptionPlan(ProductDetails product,
        IEnumerable<ProductDetails.SubscriptionOfferDetails> basePlanOffers)
    {
        // GooglePlay only returns offers the user is eligible for; among them pick the one with the
        // lowest paid price. An offer with no paid phase can not be priced and never wins.
        var subscriptionOffer = basePlanOffers
            .OrderBy(offer => offer.PricingPhases.PricingPhaseList
                .Where(pricingPhase => pricingPhase.PriceAmountMicros > 0)
                .Select(pricingPhase => pricingPhase.PriceAmountMicros)
                .DefaultIfEmpty(long.MaxValue)
                .Min())
            .First();

        // phases are ordered by payment sequence: an optional zero-price trial, an optional
        // introductory price, then the recurring base price as the last phase
        var pricingPhases = subscriptionOffer.PricingPhases.PricingPhaseList;
        var basePhase = pricingPhases.LastOrDefault();
        var currentPhase = pricingPhases.FirstOrDefault(pricingPhase => pricingPhase.PriceAmountMicros > 0);
        var trialPhase = pricingPhases.FirstOrDefault(pricingPhase => pricingPhase.PriceAmountMicros == 0);
        if (basePhase == null || currentPhase == null) {
            VhLogger.Instance.LogWarning(
                "Could not price a GooglePlay base plan. ProductId: {ProductId}, BasePlanId: {BasePlanId}",
                product.ProductId, subscriptionOffer.BasePlanId);
            return null;
        }

        var planToken = new SubscriptionPlanToken {
            ProductId = product.ProductId,
            BasePlanId = subscriptionOffer.BasePlanId,
            OfferToken = subscriptionOffer.OfferToken
        };

        return new SubscriptionPlan {
            PlanToken = JsonSerializer.Serialize(planToken),
            BasePrice = basePhase.PriceAmountMicros / 1_000_000.0,
            CurrentPrice = currentPhase.PriceAmountMicros / 1_000_000.0,
            Period = basePhase.BillingPeriod,
            TrialPeriod = trialPhase?.BillingPeriod,
            CurrencySymbol = VhUtils.GetCurrencySymbol(basePhase.PriceCurrencyCode),
            CurrencyCode = basePhase.PriceCurrencyCode
        };
    }

    private static async Task<IReadOnlyList<ProductDetails>> GetProducts(BillingClient billingClient,
        IReadOnlyList<string> productIds)
    {
        // Create a generic List to hold the product definitions
        var productsToQuery = productIds
            .Select(productId => QueryProductDetailsParams.Product.NewBuilder()
                .SetProductId(productId)
                .SetProductType(BillingClient.ProductType.Subs)
                .Build())
            .ToList();

        // Build the final params object using the list
        var productDetailsParams = QueryProductDetailsParams.NewBuilder()
            .SetProductList(productsToQuery)
            .Build();

        // Query Google Play Billing for Product Details
        var productDetailsResult = await billingClient
            .QueryProductDetailsAsync(productDetailsParams)
            .Vhc();

        // productDetailsResult.ProductDetailsList is obsolete and return null
        return [.. productDetailsResult.ProductDetails];
    }

    public async Task<PurchaseProof> Purchase(IUiContext uiContext, PurchaseParams purchaseParams,
        PurchaseAttribution attribution, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        using var partialActivityScope = AppUiContext.CreatePartialIntentScope();
        var subscriptionToken = JsonUtils.Deserialize<SubscriptionPlanToken>(purchaseParams.PlanToken);

        var billingClient = await GetSafeBillingClient(cancellationToken).Vhc();

        // Get the product details for the selected plan. Only the chosen product is queried: the plan
        // token came from GetSubscriptionPlans, so re-reading the whole catalog here would put a
        // catalog lookup — and its failure modes — inside the purchase flow for nothing.
        var products = await GetProducts(billingClient, [subscriptionToken.ProductId]).Vhc();
        var product = products.SingleOrDefault(x => x.ProductId == subscriptionToken.ProductId)
                      ?? throw new ArgumentException($"Product with id {subscriptionToken.ProductId} not found.");

        // Create the billing flow parameters
        var productParam = BillingFlowParams.ProductDetailsParams.NewBuilder()
            .SetProductDetails(product)
            .SetOfferToken(subscriptionToken.OfferToken)
            .Build();

        // Play takes the account id verbatim as the obfuscated account id
        var billingFlowParams = BillingFlowParams.NewBuilder()
            .SetObfuscatedAccountId(attribution.UserId)
            .SetProductDetailsParamsList([productParam])
            .Build();

        try {
            PurchaseState = PurchaseState.Started;

            // Play reports every purchase to ONE listener, so this provider can only track one flow
            // at a time: starting a second would leave the first caller waiting on a source nothing
            // completes. The app serializes store calls, so this is the fail-loud backstop for a
            // caller that does not.
            if (_taskCompletionSource is { Task.IsCompleted: false })
                throw new InvalidOperationException("A purchase is already in progress.");

            _taskCompletionSource = new TaskCompletionSource<PurchaseProof>();
            var billingResult = billingClient.LaunchBillingFlow(appUiContext.Activity, billingFlowParams);

            if (billingResult.ResponseCode != BillingResponseCode.Ok)
                throw GoogleBillingErrors.Create(billingResult);

            return await _taskCompletionSource.Task.WaitAsync(cancellationToken).Vhc();
        }
        catch (TaskCanceledException ex) {
            VhLogger.Instance.LogError(ex, "The google play purchase task was canceled by the user");
            throw new OperationCanceledException();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not get order id from google play LaunchBillingFlow.");
            throw;
        }
        finally {
            PurchaseState = PurchaseState.None;
        }
    }

    public async Task<PurchaseProof?> RestorePurchase(IUiContext uiContext, CancellationToken cancellationToken)
    {
        // The SILENT ownership query (lifecycle §7): reads what the device already knows and never
        // prompts, so it is safe on every sign-in as well as behind the visible Restore control.
        // Renewals still arrive via real-time developer notifications; what only this query can do
        // is hand an owned subscription to a BRAND-NEW account — the way back after a deletion.
        // The backend treats a re-presented purchase as an idempotent replay, so returning the
        // newest owned subscription either recovers it or changes nothing.
        var purchase = await GetOwnedSubscription(cancellationToken).Vhc();
        return purchase == null ? null : new PurchaseProof { Value = purchase.PurchaseToken };
    }

    /// <summary>The newest subscription this store account already owns, or null when it owns none.</summary>
    private async Task<Purchase?> GetOwnedSubscription(CancellationToken cancellationToken)
    {
        var billingClient = await GetSafeBillingClient(cancellationToken).Vhc();
        var queryPurchasesParams = QueryPurchasesParams.NewBuilder()
            .SetProductType(BillingClient.ProductType.Subs)
            .Build();

        var queryResult = await billingClient.QueryPurchasesAsync(queryPurchasesParams).Vhc();
        if (queryResult.Result.ResponseCode != BillingResponseCode.Ok)
            throw GoogleBillingErrors.Create(queryResult.Result);

        return queryResult.Purchases
            .Where(x => x.OrderId != null) // Play sets no order id while a purchase is still pending
            .OrderByDescending(x => x.PurchaseTime)
            .FirstOrDefault();
    }

    private async Task<BillingClient> GetSafeBillingClient(CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (_billingClient.Value.IsReady)
            return _billingClient.Value;

        try {
            var googleApiAvailability = GoogleApiAvailability.Instance;
            var result = googleApiAvailability.IsGooglePlayServicesAvailable(Application.Context);
            if (result != ConnectionResult.Success)
                throw GoogleBillingErrors.PlayUnavailable();

            var billingResult = await _billingClient.Value.StartConnectionAsync().Vhc();
            if (billingResult.ResponseCode != BillingResponseCode.Ok)
                throw GoogleBillingErrors.Create(billingResult);

            return _billingClient.Value;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not start connection to google play.");
            throw;
        }
    }

    public void Dispose()
    {
        if (_billingClient.IsValueCreated)
            _billingClient.Value.Dispose();
    }
}