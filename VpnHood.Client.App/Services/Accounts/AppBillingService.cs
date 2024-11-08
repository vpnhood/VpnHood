using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App.Services.Accounts;

public class AppBillingService(VpnHoodApp vpnHoodApp, IAppBillingProvider billingProvider) : IDisposable
{
    public BillingPurchaseState PurchaseState => billingProvider.PurchaseState;

    public Task<SubscriptionPlan[]> GetSubscriptionPlans()
    {
        return billingProvider.GetSubscriptionPlans();
    }

    public async Task<string> Purchase(IUiContext uiContext, string planId)
    {
        var ret = await billingProvider.Purchase(uiContext, planId).VhConfigureAwait();
        await vpnHoodApp.RefreshAccount(updateCurrentClientProfile: true).VhConfigureAwait();
        return ret;
    }

    public void Dispose()
    {
        billingProvider.Dispose();
    }
}