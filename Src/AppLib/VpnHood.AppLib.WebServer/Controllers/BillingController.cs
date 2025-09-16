using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Services.Accounts;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class BillingController : IBillingController
{
    private static AppBillingService BillingService => 
        VpnHoodApp.Instance.Services.AccountService?.BillingService ?? 
        throw new Exception("Billing service is not available at this moment.");

    public Task<SubscriptionPlan[]> GetSubscriptionPlans()
    {
        return BillingService.GetSubscriptionPlans();
    }

    public Task<string> Purchase(string planId)
    {
        return BillingService.Purchase(AppUiContext.RequiredContext, planId);
    }

    public Task<AppPurchaseOptions> GetPurchaseOptions()
    {
        return VpnHoodApp.Instance.GetPurchaseOptions();
    }
}