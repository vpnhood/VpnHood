using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.WebServer.Api;
using VpnHood.Client.Device;

namespace VpnHood.Client.App.WebServer.Controllers;

internal class BillingController : WebApiController, IBillingController
{
    public IAppBillingService BillingService => VpnHoodApp.Instance.Services.AccountService?.Billing
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