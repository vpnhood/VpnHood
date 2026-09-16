using VpnHood.AppLib.Api;
using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Api.InProcessHost;

internal class IntentsApi(VpnHoodApp app) : IIntentsApi
{
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