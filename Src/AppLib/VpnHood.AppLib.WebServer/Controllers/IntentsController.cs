using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class IntentsController : WebApiController, IIntentController
{
    private static VpnHoodApp App => VpnHoodApp.Instance;

    [Route(HttpVerbs.Get, "/status")]
    public Task<IntentStatus> GetStatus()
    {
        var status = new IntentStatus {
            IsUserReviewSupported = App.Features.IsUserReviewSupported,
            IsSystemPrivateDnsSettingsSupported = App.Services.UiProvider.IsSystemPrivateDnsSettingsSupported,
            IsRequestQuickLaunchSupported = App.Services.UiProvider.IsRequestQuickLaunchSupported,
            IsNotificationEnabled = App.Services.UiProvider.IsNotificationEnabled,
            IsRequestNotificationSupported = App.Services.UiProvider.IsRequestNotificationSupported,
            IsSystemKillSwitchSettingsSupported = App.Services.UiProvider.IsSystemKillSwitchSettingsSupported,
            IsSystemAlwaysOnSettingsSupported = App.Services.UiProvider.IsSystemAlwaysOnSettingsSupported,
            IsSystemSettingsSupported = App.Services.UiProvider.IsSystemSettingsSupported,
            IsAppSystemSettingsSupported = App.Services.UiProvider.IsAppSystemSettingsSupported,
            IsAppSystemNotificationSettingsSupported = App.Services.UiProvider.IsAppSystemNotificationSettingsSupported
        };

        return Task.FromResult(status);
    }

    [Route(HttpVerbs.Post, "/request-quick-launch")]
    public Task RequestQuickLaunch()
    {
        return App.Services.UiProvider.RequestQuickLaunch(AppUiContext.RequiredContext, CancellationToken.None);
    }

    [Route(HttpVerbs.Post, "/request-notification")]
    public Task RequestNotification()
    {
        return App.Services.UiProvider.RequestNotification(AppUiContext.RequiredContext, CancellationToken.None);
    }

    [Route(HttpVerbs.Post, "/request-user-review")]
    public Task RequestUserReview()
    {
        if (App.Services.UserReviewProvider is null)
            throw new NotSupportedException("User review is not supported.");

        return App.Services.UserReviewProvider.RequestReview(AppUiContext.RequiredContext, CancellationToken.None);
    }

    [Route(HttpVerbs.Post, "/open-system-kill-switch-settings")]
    public Task OpenSystemKillSwitchSettings()
    {
        App.Services.UiProvider.OpenSystemKillSwitchSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }


    [Route(HttpVerbs.Post, "/open-system-always-on-settings")]
    public Task OpenSystemAlwaysOnSettings()
    {
        App.Services.UiProvider.OpenSystemAlwaysOnSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }

    [Route(HttpVerbs.Post, "/open-system-settings")]
    public Task OpenSystemSettings()
    {
        App.Services.UiProvider.OpenSystemSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }

    [Route(HttpVerbs.Post, "/open-app-system-settings")]
    public Task OpenAppSystemSettings()
    {
        App.Services.UiProvider.OpenAppSystemSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }

    [Route(HttpVerbs.Post, "/open-app-system-notification-settings")]
    public Task OpenAppSystemNotificationSettings()
    {
        App.Services.UiProvider.OpenAppSystemNotificationSettings(AppUiContext.RequiredContext);
        return Task.CompletedTask;
    }
}