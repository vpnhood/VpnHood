using VpnHood.Client.App.Abstractions;

namespace VpnHood.Client.App.Store;

internal class StoreBillingService(
    IAppBillingService appBillingService, 
    IAppBillingService appBillingServiceImplementation) 
    : IAppBillingService
{
    public Task<SubscriptionPlan[]> GetSubscriptionPlans()
    {
        return appBillingService.GetSubscriptionPlans();
    }

    public Task<string> Purchase(IAppUiContext uiContext, string planId)
    {
        return appBillingService.Purchase(uiContext, planId);
    }

    public void Dispose()
    {
        appBillingService.Dispose();
    }
}