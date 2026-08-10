using System.Security.Authentication;
using System.Text.Json;
using Android.BillingClient.Api;
using Android.Gms.Common;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Droid.GooglePlay.Exceptions;
using VpnHood.Core.Client.Devices.Droid;
using VpnHood.Core.Client.Devices.UiContexts;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Droid.GooglePlay;

public class GooglePlayBillingProvider : IAppBillingProvider
{
    private readonly Lazy<BillingClient> _billingClient;
    private readonly IReadOnlyList<string> _productIds;
    private TaskCompletionSource<AppPurchaseResult>? _taskCompletionSource;
    public BillingPurchaseState PurchaseState { get; private set; }
    public string ProviderName => "GooglePlay";

    // Play's account-wide subscriptions page. Deliberately not the per-sku deep link (it needs the
    // purchased sku + package at render time, data the UI should not assemble); this page lists the
    // user's subscriptions including this app's.
    public Uri? SubscriptionManagementUrl => new("https://play.google.com/store/account/subscriptions");

    public GooglePlayBillingProvider(IReadOnlyList<string> productIds)
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

        _productIds = productIds;
    }

    private void PurchasesUpdatedListener(BillingResult billingResult, IList<Purchase> purchases)
    {
        switch (billingResult.ResponseCode) {
            case BillingResponseCode.Ok:
                var purchasedItem = purchases.FirstOrDefault();
                if (purchasedItem == null) {
                    _taskCompletionSource?.TrySetException(GoogleBillingException.Create(billingResult));
                    break;
                }

                if (purchasedItem.OrderId != null)
                    _taskCompletionSource?.TrySetResult(new AppPurchaseResult {
                        ProviderOrderId = purchasedItem.OrderId,
                        PurchaseData = purchasedItem.PurchaseToken
                    });
                else
                    // Based on Google document, orderId is null on pending state.
                    // The pending state must be handled in the UI to let the user know their subscription will be
                    // available when Google accepts payment and changes the purchase state to PURCHASES.
                    _taskCompletionSource?.TrySetException(
                        GoogleBillingException.Create(billingResult, purchasedItem.PurchaseState));
                break;

            default:
                _taskCompletionSource?.TrySetException(GoogleBillingException.Create(billingResult));
                break;
        }
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(CancellationToken cancellationToken)
    {
        var billingClient = await GetSafeBillingClient(cancellationToken).Vhc();

        // Check if the purchase subscription is supported on the user's device
        try {
            var isDeviceSupportSubscription =
                billingClient.IsFeatureSupported(BillingClient.FeatureType.Subscriptions);
            if (isDeviceSupportSubscription.ResponseCode == BillingResponseCode.FeatureNotSupported)
                throw GoogleBillingException.Create(isDeviceSupportSubscription);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not check supported feature with google play.");
            throw;
        }

        // Get products list from GooglePlay.
        try {
            var products = await GetProducts(billingClient, _productIds);

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

    public async Task<AppPurchaseResult> Purchase(IUiContext uiContext, PurchaseParams purchaseParams, CancellationToken cancellationToken)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        using var partialActivityScope = AppUiContext.CreatePartialIntentScope();
        var subscriptionToken = JsonUtils.Deserialize<SubscriptionPlanToken>(purchaseParams.PurchaseToken);

        var billingClient = await GetSafeBillingClient(cancellationToken).Vhc();

        var accountId = purchaseParams.Attribution?.AccountId
            ?? throw new AuthenticationException("Could not purchase because the purchase attribution has no account id.");

        // Get the product details for the selected plan
        var products = await GetProducts(billingClient, _productIds).Vhc();
        var product = products.SingleOrDefault(x => x.ProductId == subscriptionToken.ProductId)
                      ?? throw new ArgumentException($"Product with id {subscriptionToken.ProductId} not found.");

        // Create the billing flow parameters
        var productParam = BillingFlowParams.ProductDetailsParams.NewBuilder()
            .SetProductDetails(product)
            .SetOfferToken(subscriptionToken.OfferToken)
            .Build();

        var billingFlowParams = BillingFlowParams.NewBuilder()
            .SetObfuscatedAccountId(accountId)
            .SetProductDetailsParamsList([productParam])
            .Build();

        try {
            PurchaseState = BillingPurchaseState.Started;
            _taskCompletionSource = new TaskCompletionSource<AppPurchaseResult>();
            var billingResult = billingClient.LaunchBillingFlow(appUiContext.Activity, billingFlowParams);

            if (billingResult.ResponseCode != BillingResponseCode.Ok)
                throw GoogleBillingException.Create(billingResult);

            var purchaseResult = await _taskCompletionSource.Task.WaitAsync(cancellationToken).Vhc();
            return purchaseResult;
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
            PurchaseState = BillingPurchaseState.None;
        }
    }

    public Task<AppPurchaseResult?> RestorePurchase(IUiContext uiContext, CancellationToken cancellationToken)
    {
        // GooglePlay purchases are reconciled by the backend via real-time developer notifications
        return Task.FromResult<AppPurchaseResult?>(null);
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
                throw new GooglePlayUnavailableException();

            var billingResult = await _billingClient.Value.StartConnectionAsync().Vhc();
            if (billingResult.ResponseCode != BillingResponseCode.Ok)
                throw GoogleBillingException.Create(billingResult);

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