using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Providers;

internal class AppNotSupportedUiProvider
    : IAppUiProvider
{
    public SystemBarsInfo GetSystemBarsInfo(IUiContext uiContext) => SystemBarsInfo.Default;

    public bool IsRequestQuickLaunchSupported => false;

    public Task<bool> RequestQuickLaunch(IUiContext uiContext, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public bool IsRequestNotificationSupported => false;

    public Task<bool> RequestNotification(IUiContext uiContext, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public bool IsAlwaysOnSupported => false;
    public void OpenSystemAlwaysOn(IUiContext uiContext) =>
        throw new NotSupportedException();

    public bool IsSystemSettingsSupported => false;
    public void OpenSystemSettings(IUiContext uiContext) =>
        throw new NotSupportedException();

    public bool IsAppSystemSettingsSupported => false;
    public void OpenAppSystemSettings(IUiContext context) =>
        throw new NotSupportedException();

    public bool IsAppSystemNotificationSettingsSupported => false;
    public void OpenAppSystemNotificationSettings(IUiContext uiContext) =>
        throw new NotSupportedException();
}