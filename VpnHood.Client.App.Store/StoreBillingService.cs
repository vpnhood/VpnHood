using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Common.Utils;

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
            PurchaseState = BillingPurchaseState.Started;
            var providerOrderId = await billingService.Purchase(uiContext, planId).VhConfigureAwait();
            PurchaseState = BillingPurchaseState.Processing;
            await storeAccountService.WaitForProcessProviderOrder(providerOrderId).VhConfigureAwait();
            return providerOrderId;

        }
        finally
        {
            PurchaseState = BillingPurchaseState.None;
        } 
    }

    public BillingPurchaseState PurchaseState { get; private set; }
}