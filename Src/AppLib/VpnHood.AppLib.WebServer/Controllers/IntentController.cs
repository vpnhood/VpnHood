using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class IntentController : WebApiController, IIntentController
{
    private static VpnHoodApp App => VpnHoodApp.Instance;

    [Route(HttpVerbs.Post, "/intents/request-quick-launch")]
    public Task RequestQuickLaunch()
    {
        return App.Services.UiProvider.RequestQuickLaunch(AppUiContext.RequiredContext, CancellationToken.None);
    }

    [Route(HttpVerbs.Post, "/intents/request-notification")]
    public Task RequestNotification()
    {
        return App.Services.UiProvider.RequestNotification(AppUiContext.RequiredContext, CancellationToken.None);
    }

    [Route(HttpVerbs.Post, "/intents/request-user-review")]
    public Task RequestUserReview()
    {
        if (App.Services.UserReviewProvider is null)
            throw new NotSupportedException("User review is not supported.");

        return App.Services.UserReviewProvider.RequestReview(AppUiContext.RequiredContext, CancellationToken.None);
    }

    [Route(HttpVerbs.Post, "/intents/open-system-kill-switch-settings")]
    public Task OpenSystemKillSwitchSettings()
    {
        App.Services.UiProvider.OpenSystemKillSwitchSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }


    [Route(HttpVerbs.Post, "/intents/open-system-always-on-settings")]
    public Task OpenSystemAlwaysOnSettings()
    {
        App.Services.UiProvider.OpenSystemAlwaysOnSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }

    [Route(HttpVerbs.Post, "/intents/open-system-settings")]
    public Task OpenSystemSettings()
    {
        App.Services.UiProvider.OpenSystemSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }

    [Route(HttpVerbs.Post, "/intents/open-app-system-settings")]
    public Task OpenAppSystemSettings()
    {
        App.Services.UiProvider.OpenAppSystemSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }

    [Route(HttpVerbs.Post, "/intents/open-app-system-notification-settings")]
    public Task OpenAppSystemNotificationSettings()
    {
        App.Services.UiProvider.OpenAppSystemSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }
}