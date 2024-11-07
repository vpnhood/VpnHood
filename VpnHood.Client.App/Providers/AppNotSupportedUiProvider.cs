using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;

namespace VpnHood.Client.App.Providers;

internal class AppNotSupportedUiProvider
    : IAppUiProvider
{
    public bool IsQuickLaunchSupported => false;

    public Task<bool> RequestQuickLaunch(IUiContext uiContext, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public bool IsNotificationSupported => false;

    public Task<bool> RequestNotification(IUiContext uiContext, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public bool IsOpenAlwaysOnPageSupported => false;
    public void OpenAlwaysOnPage(IUiContext uiContext) => throw new NotSupportedException();
}