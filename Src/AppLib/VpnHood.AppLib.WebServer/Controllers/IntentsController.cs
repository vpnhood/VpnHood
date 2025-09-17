using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Extensions;
using VpnHood.Core.Client.Device.UiContexts;
using WatsonWebserver.Core;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class IntentsController : ControllerBase, IIntentController
{
    private static VpnHoodApp App => VpnHoodApp.Instance;

    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/intents/";

        mapper.AddStatic(HttpMethod.POST, baseUrl + "request-quick-launch", async ctx => {
            var res = await RequestQuickLaunch();
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "request-user-review", async ctx => {
            await RequestUserReview();
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "request-notification", async ctx => {
            var res = await RequestNotification();
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-system-kill-switch-settings", async ctx => {
            await OpenSystemKillSwitchSettings();
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-system-always-on-settings", async ctx => {
            await OpenSystemAlwaysOnSettings();
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-system-settings", async ctx => {
            await OpenSystemSettings();
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-app-system-settings", async ctx => {
            await OpenAppSystemSettings();
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-app-system-notification-settings", async ctx => {
            await OpenAppSystemNotificationSettings();
            await ctx.SendNoContent();
        });
    }

    public Task<bool> RequestQuickLaunch()
    {
        return App.Services.UiProvider.RequestQuickLaunch(AppUiContext.RequiredContext, CancellationToken.None);
    }

    public Task<bool> RequestNotification()
    {
        return App.Services.UiProvider.RequestNotification(AppUiContext.RequiredContext, CancellationToken.None);
    }

    public Task RequestUserReview()
    {
        if (App.Services.UserReviewProvider is null)
            throw new NotSupportedException("User review is not supported.");

        return App.Services.UserReviewProvider.RequestReview(AppUiContext.RequiredContext, CancellationToken.None);
    }

    public Task OpenSystemKillSwitchSettings()
    {
        App.Services.UiProvider.OpenSystemKillSwitchSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }

    public Task OpenSystemAlwaysOnSettings()
    {
        App.Services.UiProvider.OpenSystemAlwaysOnSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }

    public Task OpenSystemSettings()
    {
        App.Services.UiProvider.OpenSystemSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }

    public Task OpenAppSystemSettings()
    {
        App.Services.UiProvider.OpenAppSystemSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }

    public Task OpenAppSystemNotificationSettings()
    {
        App.Services.UiProvider.OpenAppSystemNotificationSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }
}