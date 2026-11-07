using VpnHood.AppLib.Api;
using VpnHood.AppLib.Abstractions.UiContexts;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.AppLib.App.ApiImpl;

internal sealed class IntentsApi(VpnHoodApp app) : IIntentsApi
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

    // One name, so a second report replaces the first rather than piling up in the device's cache
    public async Task ShareLog(CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream();
        await app.CopyLogToStream(stream).Vhc();
        app.Services.DeviceUiProvider.ShareFile(AppUiContext.RequiredContext, "report.txt", stream.ToArray());
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