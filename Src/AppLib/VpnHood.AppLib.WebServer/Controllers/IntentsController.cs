using VpnHood.AppLib.WebServer.Api;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.WebServer.Controllers;

internal class IntentsController : IIntentController
{
    private static VpnHoodApp App => VpnHoodApp.Instance;

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