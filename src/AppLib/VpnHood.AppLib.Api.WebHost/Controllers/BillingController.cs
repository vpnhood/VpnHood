using VpnHood.AppLib.Api.WebHost.Helpers;
using HttpMethod = WatsonWebserver.Core.HttpMethod;
using VpnHood.AppLib.Api.Billing;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppLib.Api.WebHost.Controllers;

internal class BillingController(IBillingApi api) : ControllerBase
{
    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/billing/";

        mapper.AddStatic(HttpMethod.GET, baseUrl + "subscription-plans", async ctx => {
            var res = await api.GetSubscriptionPlans(ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "purchase", async ctx => {
            var purchaseParams = ctx.ReadJson<PurchaseParams>();
            await api.Purchase(purchaseParams, ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "restore-purchase", async ctx => {
            var res = await api.RestorePurchase(ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "subscription-management", async ctx => {
            await api.OpenSubscriptionManagement(ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });
    }
}
