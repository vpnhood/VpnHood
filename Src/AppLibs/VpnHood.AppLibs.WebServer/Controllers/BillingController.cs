using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using VpnHood.AppLibs.Abstractions;
using VpnHood.AppLibs.Services.Accounts;
using VpnHood.AppLibs.WebServer.Api;
using VpnHood.Core.Client.Device;

namespace VpnHood.AppLibs.WebServer.Controllers;

internal class BillingController : WebApiController, IBillingController
{
    private static AppBillingService BillingService => VpnHoodApp.Instance.Services.AccountService?.BillingService
                                                ?? throw new Exception("Billing service is not available at this moment.");

    [Route(HttpVerbs.Get, "/subscription-plans")]
    public Task<SubscriptionPlan[]> GetSubscriptionPlans()
    {
        return BillingService.GetSubscriptionPlans();
    }

    [Route(HttpVerbs.Post, "/purchase")]
    public Task<string> Purchase([QueryField] string planId)
    {
        return BillingService.Purchase(ActiveUiContext.RequiredContext, planId);
    }
}