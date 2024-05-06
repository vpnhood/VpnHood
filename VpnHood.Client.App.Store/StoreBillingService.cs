using VpnHood.Client.App.Abstractions;

namespace VpnHood.Client.App.Store;

internal class StoreBillingService(IAppBillingService billingService)  
    : IAppBillingService
{
    public Task<SubscriptionPlan[]> GetSubscriptionPlans()
    {
        return billingService.GetSubscriptionPlans();
    }

    public async Task<string> Purchase(IAppUiContext uiContext, string planId)
    {
        var ret = await billingService.Purchase(uiContext, planId);
        await VpnHoodApp.Instance.UpdateAccount();
        return ret;
    }

    public void Dispose()
    {
        billingService.Dispose();
    }
}