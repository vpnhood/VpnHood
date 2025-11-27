using System.Globalization;
using System.Text.Json;
using Android.BillingClient.Api;
using Android.Gms.Common;
using Microsoft.Extensions.Logging;
using Org.Apache.Http.Authentication;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Droid.GooglePlay.Exceptions;
using VpnHood.Core.Client.Device.Droid;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Droid.GooglePlay;

public class GooglePlayBillingProvider : IAppBillingProvider
{
    private readonly Lazy<BillingClient> _billingClient;
    private readonly IAppAuthenticationProvider _authenticationProvider;
    private readonly string[] _productIds;
    private TaskCompletionSource<string>? _taskCompletionSource;
    public BillingPurchaseState PurchaseState { get; private set; }
    public string ProviderName => "GooglePlay";

    public GooglePlayBillingProvider(IAppAuthenticationProvider authenticationProvider, string[] productIds)
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

        _authenticationProvider = authenticationProvider;
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
                    _taskCompletionSource?.TrySetResult(purchasedItem.OrderId);
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

    public async Task<SubscriptionPlan[]> GetSubscriptionPlans()
    {
        var billingClient = await GetSafeBillingClient().Vhc();

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

            // We chose the subscriptionOfferDetails which contains the lowest PricingPhaseList
            // Then build the SubscriptionPlan list from it.
            var subscriptionPlans = products.Select(product => {
                // Get the offer details with the lowest price
                var subscriptionOffer = product.GetSubscriptionOfferDetails()?
                    .OrderBy(od => od.PricingPhases.PricingPhaseList.Min(pp => pp.PriceAmountMicros))
                    .FirstOrDefault();

                // Get the pricing phases for that offer
                var pricingPhases = subscriptionOffer?.PricingPhases.PricingPhaseList;
                if (subscriptionOffer is null || pricingPhases is null) {
                    VhLogger.Instance.LogWarning("Could not get GooglePlay pricing phases for product id {ProductId}",
                        product.ProductId);
                    return null;
                }

                // order pricing phases by price amount descending, the first is base price, the rest are discounted prices if any
                var planPrices = pricingPhases.OrderByDescending(pricingPhase => pricingPhase.PriceAmountMicros)
                    .ToArray();
                if (!planPrices.Any()) {
                    VhLogger.Instance.LogWarning("Could not get GooglePlay plan prices for product id {ProductId}",
                        product.ProductId);
                    return null;
                }

                var planToken = new SubscriptionPlanToken {
                    ProductId = product.ProductId,
                    BasePlanId = subscriptionOffer.BasePlanId,
                    OfferToken = subscriptionOffer.OfferToken
                };

                return new SubscriptionPlan {
                    PlanToken = JsonSerializer.Serialize(planToken),
                    BasePrice = planPrices.First().PriceAmountMicros / 1_000_000.0,
                    CurrentPrice = planPrices.Last().PriceAmountMicros / 1_000_000.0,
                    Period = planPrices.First().BillingPeriod,
                    CurrencySymbol = planPrices.First().FormattedPrice.Substring(0,1) //TODO: trudy check
                };
            }).Where(plan => plan != null).ToArray();

            // we already filtered null plans, so this cast is safe
            return subscriptionPlans!;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not get products from google play.");
            throw;
        }
    }

    private static async Task<ProductDetails[]> GetProducts(BillingClient billingClient, string[] productIds)
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

        return productDetailsResult.ProductDetailsList.ToArray();
    }

    public async Task<string> Purchase(IUiContext uiContext, PurchaseParams purchaseParams)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        using var partialActivityScope = AppUiContext.CreatePartialIntentScope();
        var subscriptionToken = JsonUtils.Deserialize<SubscriptionPlanToken>(purchaseParams.PurchaseToken);

        var billingClient = await GetSafeBillingClient().Vhc();

        if (_authenticationProvider.UserId == null)
            throw new AuthenticationException();

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
            .SetObfuscatedAccountId(_authenticationProvider.UserId)
            .SetProductDetailsParamsList([productParam])
            .Build();

        try {
            PurchaseState = BillingPurchaseState.Started;
            _taskCompletionSource = new TaskCompletionSource<string>();
            var billingResult = billingClient.LaunchBillingFlow(appUiContext.Activity, billingFlowParams);

            if (billingResult.ResponseCode != BillingResponseCode.Ok)
                throw GoogleBillingException.Create(billingResult);

            var orderId = await _taskCompletionSource.Task.Vhc();
            return orderId;
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

    private async Task<BillingClient> GetSafeBillingClient()
    {
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