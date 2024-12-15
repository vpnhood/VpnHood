using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.Utils;

namespace VpnHood.AppLib.Services.Accounts;

public class AppBillingService(AppAccountService accountService, IAppBillingProvider billingProvider) : IDisposable
{
    public BillingPurchaseState PurchaseState => billingProvider.PurchaseState;

    public Task<SubscriptionPlan[]> GetSubscriptionPlans()
    {
        return billingProvider.GetSubscriptionPlans();
    }

    public async Task<string> Purchase(IUiContext uiContext, string planId)
    {
        var ret = await billingProvider.Purchase(uiContext, planId).VhConfigureAwait();
        await accountService.Refresh(updateCurrentClientProfile: true).VhConfigureAwait();
        return ret;
    }

    public void Dispose()
    {
        billingProvider.Dispose();
    }
}