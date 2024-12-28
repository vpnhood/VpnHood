using Android.BillingClient.Api;
using Microsoft.Extensions.Logging;
using Org.Apache.Http.Authentication;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.Device.Droid;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Utils;

namespace VpnHood.AppLib.Droid.GooglePlay;

public class GooglePlayBillingProvider : IAppBillingProvider
{
    private readonly BillingClient _billingClient;
    private readonly IAppAuthenticationProvider _authenticationProvider;
    private ProductDetails? _productDetails;
    private IList<ProductDetails.SubscriptionOfferDetails>? _subscriptionOfferDetails;
    private TaskCompletionSource<string>? _taskCompletionSource;
    public BillingPurchaseState PurchaseState { get; private set; }

    public GooglePlayBillingProvider(IAppAuthenticationProvider authenticationProvider)
    {
        var builder = BillingClient.NewBuilder(Application.Context);
        builder.SetListener(PurchasesUpdatedListener);
        
        // We don't have the On-Time Purchase in this app, but if EnablePendingPurchases is not implemented,
        // we get the error "Pending purchases for one-time products must be supported."
        _billingClient = builder.EnablePendingPurchases(
            PendingPurchasesParams.NewBuilder().EnableOneTimeProducts().Build()
            ).Build();
        
        _authenticationProvider = authenticationProvider;
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
                    _taskCompletionSource?.TrySetException(GoogleBillingException.Create(billingResult, purchasedItem.PurchaseState));
                break;
            
            default:
                _taskCompletionSource?.TrySetException(GoogleBillingException.Create(billingResult));
                break;
        }
    }

    public async Task<SubscriptionPlan[]> GetSubscriptionPlans()
    {
        await EnsureConnected().VhConfigureAwait();

        // Check if the purchase subscription is supported on the user's device
        try {
            var isDeviceSupportSubscription =
                _billingClient.IsFeatureSupported(BillingClient.FeatureType.Subscriptions);
            if (isDeviceSupportSubscription.ResponseCode == BillingResponseCode.FeatureNotSupported)
                throw GoogleBillingException.Create(isDeviceSupportSubscription);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not check supported feature with google play.");
            throw;
        }

        // Set list of the created products in the GooglePlay.
        var productDetailsParams = QueryProductDetailsParams.NewBuilder()
            .SetProductList([
                QueryProductDetailsParams.Product.NewBuilder()
                    .SetProductId("general_subscription")
                    .SetProductType(BillingClient.ProductType.Subs)
                    .Build()
            ])
            .Build();

        // Get products list from GooglePlay.
        try {
            var response = await _billingClient.QueryProductDetailsAsync(productDetailsParams).ConfigureAwait(false);
            if (response.Result.ResponseCode != BillingResponseCode.Ok)
                throw GoogleBillingException.Create(response.Result);

            _productDetails = response.ProductDetails.First();
            _subscriptionOfferDetails = _productDetails.GetSubscriptionOfferDetails()
                                        ?? throw new Exception("Could not get subscription offer details.");

            var subscriptionPlans = _subscriptionOfferDetails
                .Where(plan => plan.PricingPhases.PricingPhaseList.Any())
                .Select(plan => new SubscriptionPlan {
                    SubscriptionPlanId = plan.BasePlanId,
                    PlanPrice = plan.PricingPhases.PricingPhaseList.First().FormattedPrice
                })
                .ToArray();

            return subscriptionPlans;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not get products from google play.");
            throw;
        }
    }

    public async Task<string> Purchase(IUiContext uiContext, string planId)
    {
        var appUiContext = (AndroidUiContext)uiContext;
        await EnsureConnected().ConfigureAwait(false);

        if (_authenticationProvider.UserId == null)
            throw new AuthenticationException();

        var offerToken = _subscriptionOfferDetails == null
            ? throw new NullReferenceException("Could not found subscription offer details.")
            : _subscriptionOfferDetails
                .Where(x => x.BasePlanId == planId)
                .Select(x => x.OfferToken)
                .Single();

        var productDetailsParam = BillingFlowParams.ProductDetailsParams.NewBuilder()
            .SetProductDetails(_productDetails ?? throw new NullReferenceException("Could not found product details."))
            .SetOfferToken(offerToken)
            .Build();

        var billingFlowParams = BillingFlowParams.NewBuilder()
            .SetObfuscatedAccountId(_authenticationProvider.UserId)
            .SetProductDetailsParamsList([productDetailsParam])
            .Build();

        try {
            PurchaseState = BillingPurchaseState.Started;
            _taskCompletionSource = new TaskCompletionSource<string>();
            var billingResult = _billingClient.LaunchBillingFlow(appUiContext.Activity, billingFlowParams);

            if (billingResult.ResponseCode != BillingResponseCode.Ok)
                throw GoogleBillingException.Create(billingResult);

            var orderId = await _taskCompletionSource.Task.ConfigureAwait(false);
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

    private async Task EnsureConnected()
    {
        if (_billingClient.IsReady)
            return;

        try {
            var billingResult = await _billingClient.StartConnectionAsync().ConfigureAwait(false);
            if (billingResult.ResponseCode != BillingResponseCode.Ok)
                throw GoogleBillingException.Create(billingResult);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not start connection to google play.");
            throw;
        }
    }

    public void Dispose()
    {
        _billingClient.Dispose();
    }
}