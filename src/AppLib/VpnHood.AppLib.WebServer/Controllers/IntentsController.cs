using VpnHood.AppLib.WebServer.Api;
using VpnHood.AppLib.WebServer.Helpers;
using VpnHood.Core.Client.Devices.UiContexts;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class IntentsController(VpnHoodApp app) : ControllerBase, IIntentController
{
    public override void AddRoutes(IRouteMapper mapper)
    {
        const string baseUrl = "/api/intents/";

        mapper.AddStatic(HttpMethod.POST, baseUrl + "request-quick-launch", async ctx => {
            var res = await RequestQuickLaunch(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "request-user-review", async ctx => {
            await RequestUserReview(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "request-notification", async ctx => {
            var res = await RequestNotification(ctx.Token);
            await ctx.SendJson(res);
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-kill-switch-settings", async ctx => {
            await OpenKillSwitchSettings(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-always-on-settings", async ctx => {
            await OpenAlwaysOnSettings(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-settings", async ctx => {
            await OpenSettings(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-app-settings", async ctx => {
            await OpenAppSettings(ctx.Token);
            await ctx.SendNoContent();
        });

        mapper.AddStatic(HttpMethod.POST, baseUrl + "open-app-notification-settings", async ctx => {
            await OpenAppNotificationSettings(ctx.Token);
            await ctx.SendNoContent();
        });
    }

    public Task<bool> RequestQuickLaunch(CancellationToken cancellationToken)
    {
        return app.Services.DeviceUiProvider.RequestQuickLaunch(AppUiContext.RequiredContext, cancellationToken);
    }

    public Task<bool> RequestNotification(CancellationToken cancellationToken)
    {
        return app.Services.DeviceUiProvider.RequestNotification(AppUiContext.RequiredContext, cancellationToken);
    }

    public Task RequestUserReview(CancellationToken cancellationToken)
    {
        if (app.Services.UserReviewProvider is null)
            throw new NotSupportedException("User review is not supported.");

        return app.Services.UserReviewProvider.RequestReview(AppUiContext.RequiredContext, cancellationToken);
    }

    public Task OpenKillSwitchSettings(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        app.Services.DeviceUiProvider.OpenKillSwitchSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }

    public Task OpenAlwaysOnSettings(CancellationToken cancellationToken)
    {
        app.Services.DeviceUiProvider.OpenAlwaysOnSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }

    public Task OpenSettings(CancellationToken cancellationToken)
    {
        app.Services.DeviceUiProvider.OpenSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }

    public Task OpenAppSettings(CancellationToken cancellationToken)
    {
        app.Services.DeviceUiProvider.OpenAppSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }

    public Task OpenAppNotificationSettings(CancellationToken cancellationToken)
    {
        app.Services.DeviceUiProvider.OpenAppNotificationSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }
}