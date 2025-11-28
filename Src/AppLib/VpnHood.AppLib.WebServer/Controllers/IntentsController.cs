using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Helpers;
using VpnHood.Core.Client.Device.UiContexts;
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

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-kill-switch-settings", async ctx => {
            await OpenKillSwitchSettings();
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-always-on-settings", async ctx => {
            await OpenAlwaysOnSettings();
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-settings", async ctx => {
            await OpenSettings();
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-app-settings", async ctx => {
            await OpenAppSettings();
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-app-notification-settings", async ctx => {
            await OpenAppNotificationSettings();
            await ctx.SendNoContent();
        });
    }

    public Task<bool> RequestQuickLaunch()
    {
        return App.Services.DeviceUiProvider.RequestQuickLaunch(AppUiContext.RequiredContext, CancellationToken.None);
    }

    public Task<bool> RequestNotification()
    {
        return App.Services.DeviceUiProvider.RequestNotification(AppUiContext.RequiredContext, CancellationToken.None);
    }

    public Task RequestUserReview()
    {
        if (App.Services.UserReviewProvider is null)
            throw new NotSupportedException("User review is not supported.");

        return App.Services.UserReviewProvider.RequestReview(AppUiContext.RequiredContext, CancellationToken.None);
    }

    public Task OpenKillSwitchSettings()
    {
        App.Services.DeviceUiProvider.OpenKillSwitchSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }

    public Task OpenAlwaysOnSettings()
    {
        App.Services.DeviceUiProvider.OpenAlwaysOnSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }

    public Task OpenSettings()
    {
        App.Services.DeviceUiProvider.OpenSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }

    public Task OpenAppSettings()
    {
        App.Services.DeviceUiProvider.OpenAppSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }

    public Task OpenAppNotificationSettings()
    {
        App.Services.DeviceUiProvider.OpenAppNotificationSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }
}