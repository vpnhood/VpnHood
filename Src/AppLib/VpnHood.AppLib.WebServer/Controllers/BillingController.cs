using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Services.Accounts;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Helpers;
using VpnHood.Core.Client.Device.UiContexts;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class BillingController : ControllerBase, IBillingController
{
    private static AppBillingService BillingService =>
        VpnHoodApp.Instance.Services.AccountService?.BillingService ??
        throw new Exception("Billing service is not available at this moment.");

    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/billing/";

        mapper.AddStatic(HttpMethod.GET, baseUrl + "subscription-plans", async ctx => {
            var res = await GetSubscriptionPlans();
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "purchase", async ctx => {
            var purchaseParams = ctx.ReadJson<PurchaseParams>();
            var res = await Purchase(purchaseParams);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.GET, baseUrl + "purchase-options", async ctx => {
            var res = await GetPurchaseOptions();
            await ctx.SendJson(res);
        });
    }

    public Task<SubscriptionPlan[]> GetSubscriptionPlans()
    {
        return BillingService.GetSubscriptionPlans();
    }

    public Task<string> Purchase(PurchaseParams purchaseParams)
    {
        return BillingService.Purchase(AppUiContext.RequiredContext, purchaseParams);
    }

    public Task<AppPurchaseOptions> GetPurchaseOptions()
    {
        return VpnHoodApp.Instance.GetPurchaseOptions();
    }
}