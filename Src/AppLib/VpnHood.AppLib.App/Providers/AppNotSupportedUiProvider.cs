using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device;

namespace VpnHood.AppLib.Providers;

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