using VpnHood.AppLib.Api.WebHost.Helpers;
using VpnHood.Net.Toolkit.Extensions;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.Api.WebHost.Controllers;

internal class IntentsController(IIntentsApi api) : ControllerBase
{
    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/intents/";

        mapper.AddStatic(HttpMethod.POST, baseUrl + "request-quick-launch", async ctx => {
            var res = await api.RequestQuickLaunch(ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "request-user-review", async ctx => {
            await api.RequestUserReview(ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "request-notification", async ctx => {
            var res = await api.RequestNotification(ctx.Token).Vhc();
            await ctx.SendJson(res).Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-kill-switch-settings", async ctx => {
            await api.OpenKillSwitchSettings(ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-always-on-settings", async ctx => {
            await api.OpenAlwaysOnSettings(ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-settings", async ctx => {
            await api.OpenSettings(ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-app-settings", async ctx => {
            await api.OpenAppSettings(ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-app-notification-settings", async ctx => {
            await api.OpenAppNotificationSettings(ctx.Token).Vhc();
            await ctx.SendNoContent().Vhc();
        });
    }
}
