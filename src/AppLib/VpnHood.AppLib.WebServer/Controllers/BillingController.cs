using VpnHood.AppLib.Abstractions.Billing;
using VpnHood.AppLib.Services.Accounts;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Helpers;
using VpnHood.Core.Client.Devices.UiContexts;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class BillingController(VpnHoodApp app) : ControllerBase, IBillingController
{
    private BillingService BillingService =>
        app.Services.AccountService?.BillingService ??
        throw new Exception("Billing service is not available at this moment.");

    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/billing/";

        mapper.AddStatic(HttpMethod.GET, baseUrl + "subscription-plans", async ctx => {
            var res = await GetSubscriptionPlans(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "purchase", async ctx => {
            var purchaseParams = ctx.ReadJson<PurchaseParams>();
            await Purchase(purchaseParams, ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "restore-purchase", async ctx => {
            var res = await RestorePurchase(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "subscription-management", async ctx => {
            await OpenSubscriptionManagement(ctx.Token);
            await ctx.SendNoContent();
        });
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlans(CancellationToken cancellationToken)
    {
        return BillingService.GetSubscriptionPlans(cancellationToken);
    }

    public Task Purchase(PurchaseParams purchaseParams, CancellationToken cancellationToken)
    {
        return BillingService.Purchase(AppUiContext.RequiredContext, purchaseParams, cancellationToken);
    }

    public Task<bool> RestorePurchase(CancellationToken cancellationToken)
    {
        return BillingService.RestorePurchase(AppUiContext.RequiredContext, cancellationToken);
    }

    public Task OpenSubscriptionManagement(CancellationToken cancellationToken)
    {
        return BillingService.OpenSubscriptionManagement(AppUiContext.RequiredContext, cancellationToken);
    }
}