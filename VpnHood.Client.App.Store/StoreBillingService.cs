using VpnHood.Client.App.Abstractions;

namespace VpnHood.Client.App.Store;

public class StoreBillingService(StoreAccountService storeAccountService, IAppBillingService billingService) 
    : IAppBillingService
{
    public void Dispose()
    {
        billingService.Dispose();
    }

    public Task<SubscriptionPlan[]> GetSubscriptionPlans()
    {
        return billingService.GetSubscriptionPlans();
    }

    public async Task<string> Purchase(IAppUiContext uiContext, string planId)
    {
         var providerOrderId = await billingService.Purchase(uiContext, planId);
         await storeAccountService.WaitForProcessProviderOrder(providerOrderId);
         return providerOrderId;
    }
}