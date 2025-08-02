using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Providers;

internal class AppNotSupportedUiProvider
    : IAppUiProvider
{
    public SystemBarsInfo GetSystemBarsInfo(IUiContext uiContext) => SystemBarsInfo.Default;

    public bool IsQuickLaunchSupported => false;

    public Task<bool> RequestQuickLaunch(IUiContext uiContext, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public bool IsNotificationSupported => false;

    public Task<bool> RequestNotification(IUiContext uiContext, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public bool IsAlwaysOnSupported => false;
    public void RequestAlwaysOn(IUiContext uiContext) => throw new NotSupportedException();
}