using VpnHood.Client.App.Abstractions;

namespace VpnHood.Client.App.Services;

internal class AppBillingService(VpnHoodApp vpnHoodApp, IAppBillingService billingService)
    : IAppBillingService
{
    public string? PurchaseState => billingService.PurchaseState;

    public Task<SubscriptionPlan[]> GetSubscriptionPlans()
    {
        return billingService.GetSubscriptionPlans();
    }

    public async Task<string> Purchase(IAppUiContext uiContext, string planId)
    {
        var ret = await billingService.Purchase(uiContext, planId);
        await vpnHoodApp.RefreshAccount();
        return ret;
    }


    public void Dispose()
    {
        billingService.Dispose();
    }
}