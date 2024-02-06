using Android.BillingClient.Api;
using Org.Apache.Http.Authentication;
using VpnHood.Client.App.Abstractions;

namespace VpnHood.Client.App.Droid.GooglePlay;

public class GooglePlayBillingService: IAppBillingService
{
    private readonly BillingClient _billingClient;
    private readonly Activity _activity;
    private readonly IAppAuthenticationService _authenticationService;
    private ProductDetails? _productDetails;
    private IList<ProductDetails.SubscriptionOfferDetails>? _subscriptionOfferDetails;
    private TaskCompletionSource<string>? _taskCompletionSource;
    private GooglePlayBillingService(Activity activity, IAppAuthenticationService authenticationService)
    {
        var builder = BillingClient.NewBuilder(activity);
        builder.SetListener(PurchasesUpdatedListener);
        _billingClient = builder.EnablePendingPurchases().Build();
        _activity = activity;
        _authenticationService = authenticationService;
    }

    public static GooglePlayBillingService Create(Activity activity, IAppAuthenticationService authenticationService)
    {
        return new GooglePlayBillingService(activity, authenticationService);
    }

    private void PurchasesUpdatedListener(BillingResult billingResult, IList<Purchase> purchases)
    {
        switch (billingResult.ResponseCode)
        {
            case BillingResponseCode.Ok:
                if (purchases.Any())
                    _taskCompletionSource?.TrySetResult(purchases.First().OrderId);
                else
                    _taskCompletionSource?.TrySetException(new Exception("There is no any order."));
                break;
            case BillingResponseCode.UserCancelled:
                _taskCompletionSource?.TrySetCanceled();
                break;  
            default:
                _taskCompletionSource?.TrySetException(CreateBillingResultException(billingResult));
                break;
        }
    }

    public async Task<SubscriptionPlan[]> GetSubscriptionPlans()
    {
        await EnsureConnected();

        var isDeviceSupportSubscription = _billingClient.IsFeatureSupported("subscriptions");//TODO Check parameter
        if (isDeviceSupportSubscription.ResponseCode == BillingResponseCode.FeatureNotSupported)
            throw new NotImplementedException();

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
        var response = await _billingClient.QueryProductDetailsAsync(productDetailsParams);
        if (response.Result.ResponseCode != BillingResponseCode.Ok) throw new Exception($"Could not get products from google. BillingResponseCode: {response.Result.ResponseCode}");
        if (!response.ProductDetails.Any()) throw new Exception($"Product list is empty. ProductList: {response.ProductDetails}");

        var productDetails = response.ProductDetails.First();
        _productDetails = productDetails;

        var plans = productDetails.GetSubscriptionOfferDetails();
        _subscriptionOfferDetails = plans;

        var subscriptionPlans = plans
            .Where(plan => plan.PricingPhases.PricingPhaseList.Any())
            .Select(plan => new SubscriptionPlan()
            {
                SubscriptionPlanId = plan.BasePlanId,
                PlanPrice = plan.PricingPhases.PricingPhaseList.First().FormattedPrice,
            })
            .ToArray();

        return subscriptionPlans;
    }

    public async Task<string> Purchase(string planId)
    {
        await EnsureConnected();

        if (_authenticationService.UserId == null)
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
            .SetObfuscatedAccountId(_authenticationService.UserId)
            .SetProductDetailsParamsList([productDetailsParam])
            .Build();


        var billingResult = _billingClient.LaunchBillingFlow(_activity, billingFlowParams);

        if (billingResult.ResponseCode != BillingResponseCode.Ok)
            throw CreateBillingResultException(billingResult);

        _taskCompletionSource = new TaskCompletionSource<string>();
        var orderId = await _taskCompletionSource.Task;
        return orderId;
    }

    private async Task EnsureConnected()
    {
        if (_billingClient.IsReady)
         return;

        var billingResult = await _billingClient.StartConnectionAsync();

        if (billingResult.ResponseCode != BillingResponseCode.Ok)
            throw new Exception(billingResult.DebugMessage);
    }

    public void Dispose()
    {
        _billingClient.Dispose();
    }

    private static Exception CreateBillingResultException(BillingResult billingResult)
    {
        if (billingResult.ResponseCode == BillingResponseCode.Ok)
            throw new InvalidOperationException("Response code should be not OK.");

        return new Exception(billingResult.DebugMessage)
        {
            Data = { { "ResponseCode", billingResult.ResponseCode } }
        };
    }
}