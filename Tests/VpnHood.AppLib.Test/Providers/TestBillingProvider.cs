using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Test.Providers;

internal class TestBillingProvider : IAppBillingProvider
{
    
    public Exception? PurchaseException { get; set; }
    public Exception? SubscriptionPlanException { get; set; }

    public string ProviderName => "Test";

    public async Task<SubscriptionPlan[]> GetSubscriptionPlans()
    {
        if (SubscriptionPlanException != null)
            throw SubscriptionPlanException;

        await Task.CompletedTask;
        return [
            new SubscriptionPlan {
                DiscountedPrice = ["10"],
                OfferToken = "test",
                SubscriptionPlanId = "test"
            }
        ];
    }
    
    public async Task<string> Purchase(IUiContext uiContext, string planId, string offerToken)
    {
        if (PurchaseException != null)
            throw PurchaseException;

        await Task.CompletedTask;
        return Guid.NewGuid().ToString();
    }

    public BillingPurchaseState PurchaseState => BillingPurchaseState.None;

    public void Dispose()
    {
    }
}