using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;

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

    public async Task<string> Purchase(IUiContext uiContext, string planId)
    {

        try
        {
            PurchaseState = "started";
            var providerOrderId = await billingService.Purchase(uiContext, planId);
            PurchaseState = "Processing";
            await storeAccountService.WaitForProcessProviderOrder(providerOrderId);
            return providerOrderId;

        }
        finally
        {
            PurchaseState = null;
        } 
    }

    public string? PurchaseState { get; private set; }
}