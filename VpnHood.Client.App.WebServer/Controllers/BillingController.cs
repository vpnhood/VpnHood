using System.Diagnostics.CodeAnalysis;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using VpnHood.Client.App.Abstractions;
using VpnHood.Client.App.WebServer.Api;

namespace VpnHood.Client.App.WebServer.Controllers;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
internal class BillingController : WebApiController, IBillingController
{
    public IAppBillingService BillingService => VpnHoodApp.Instance.AccountService?.Billing
        ?? throw new Exception("Billing service is not available at this moment.");

    [Route(HttpVerbs.Get, "/subscription-plans")]
    public Task<SubscriptionPlan[]> GetSubscriptionPlans()
    {
        return BillingService.GetSubscriptionPlans();
    }
}