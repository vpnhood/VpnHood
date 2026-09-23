using VpnHood.AppLib.Api.WebHost.Helpers;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.Api.WebHost.Controllers;

internal class IntentsController(IIntentsApi api) : ControllerBase
{
    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/intents/";

        mapper.AddStatic(HttpMethod.POST, baseUrl + "request-quick-launch", async ctx => {
            var res = await api.RequestQuickLaunch(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "request-user-review", async ctx => {
            await api.RequestUserReview(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "request-notification", async ctx => {
            var res = await api.RequestNotification(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-kill-switch-settings", async ctx => {
            await api.OpenKillSwitchSettings(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-always-on-settings", async ctx => {
            await api.OpenAlwaysOnSettings(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-settings", async ctx => {
            await api.OpenSettings(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-app-settings", async ctx => {
            await api.OpenAppSettings(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-app-notification-settings", async ctx => {
            await api.OpenAppNotificationSettings(ctx.Token);
            await ctx.SendNoContent();
        });
    }
}
