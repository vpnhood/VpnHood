using Android.BillingClient.Api;
using Android.Content;
using VpnHood.Client.App.Abstractions;

namespace VpnHood.Client.App.Droid.Connect;

public class GoogleBillingService: IAppBillingService
{
    private readonly BillingClient _billingClient;
    private GoogleBillingService(Context context)
    {
        var builder = BillingClient.NewBuilder(context);
        builder.SetListener(PurchasesUpdatedListener);
        _billingClient = builder.EnablePendingPurchases().Build();
    }

    public static GoogleBillingService Create(Context context)
    {
        return new GoogleBillingService(context);
    }


    private void PurchasesUpdatedListener(BillingResult billingResult, IList<Purchase> purchases)
    {
        throw new NotImplementedException();
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
                    .SetProductId("general_subscription") //TODO Change product id
                    .SetProductType(BillingClient.ProductType.Subs)
                    .Build()
            ])
            .Build();

        // Get products list from GooglePlay.
        var response = await _billingClient.QueryProductDetailsAsync(productDetailsParams);
        if (response.Result.ResponseCode != BillingResponseCode.Ok) throw new Exception($"Could not get products from google. BillingResponseCode: {response.Result.ResponseCode}");
        if (!response.ProductDetails.Any()) throw new Exception($"Product list is empty. ProductList: {response.ProductDetails}");

        var productDetails = response.ProductDetails.First();

        var plans = productDetails.GetSubscriptionOfferDetails();

        var subscriptionPlans = plans
            .Where(plan => plan.PricingPhases.PricingPhaseList.Any())
            .Select(plan => new SubscriptionPlan()
            {
                SubscriptionPlanId = plan.BasePlanId,
                PriceAmount = plan.PricingPhases.PricingPhaseList.First().PriceAmountMicros,
                PriceCurrency = plan.PricingPhases.PricingPhaseList.First().PriceCurrencyCode
            })
            .ToArray();

        return subscriptionPlans;
    }

    private async Task EnsureConnected()
    {
        if (_billingClient.IsReady)
         return;

        var billingResult = await _billingClient.StartConnectionAsync();

        if (billingResult.ResponseCode != BillingResponseCode.Ok)
            throw new Exception(billingResult.DebugMessage);
    }
}