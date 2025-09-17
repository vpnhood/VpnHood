using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Services.Accounts;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.Core.Client.Device.UiContexts;
using WatsonWebserver.Core;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class BillingController : ControllerBase, IBillingController
{
    private static AppBillingService BillingService => 
        VpnHoodApp.Instance.Services.AccountService?.BillingService ?? 
        throw new Exception("Billing service is not available at this moment.");

    public override void AddRoutes(IRouteMapper mapper)
    {
        mapper.AddStatic(HttpMethod.GET, "/api/billing/subscription-plans", async ctx => {
            var res = await GetSubscriptionPlans();
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, "/api/billing/purchase", async ctx => {
            var planId = ctx.GetQueryParameterString("planId") ?? string.Empty;
            var res = await Purchase(planId);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.GET, "/api/billing/purchase-options", async ctx => {
            var res = await GetPurchaseOptions();
            await ctx.SendJson(res);
        });
    }

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