using VpnHood.AppFramework.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Common.Utils;

namespace VpnHood.AppFramework.Services.Accounts;

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