using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.Services;

internal class AppBillingService(VpnHoodApp vpnHoodApp, IAppBillingService billingService)
    : IAppBillingService
{
    public BillingPurchaseState PurchaseState => billingService.PurchaseState;

    public Task<SubscriptionPlan[]> GetSubscriptionPlans()
    {
        return billingService.GetSubscriptionPlans();
    }

    public async Task<string> Purchase(IUiContext uiContext, string planId)
    {
        var ret = await billingService.Purchase(uiContext, planId).VhConfigureAwait();
        await vpnHoodApp.RefreshAccount(updateCurrentClientProfile: true).VhConfigureAwait();
        return ret;
    }


    public void Dispose()
    {
        billingService.Dispose();
    }
}