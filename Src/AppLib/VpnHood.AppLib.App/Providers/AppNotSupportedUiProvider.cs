using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Providers;

internal class AppNotSupportedUiProvider
    : IAppUiProvider
{
    public PrivateDns? GetSystemPrivateDns() => null;
    public SystemBarsInfo GetSystemBarsInfo(IUiContext uiContext) => SystemBarsInfo.Default;

    public bool IsQuickLaunchSupported => false;
    public bool IsRequestQuickLaunchSupported => false;

    public Task<bool> RequestQuickLaunch(IUiContext uiContext, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public bool? IsNotificationEnabled => false;

    public bool IsRequestNotificationSupported => false;

    public Task<bool> RequestNotification(IUiContext uiContext, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public bool IsSystemPrivateDnsSettingsSupported => false;

    public bool IsSystemSettingsSupported => false;
    public void OpenSystemSettings(IUiContext uiContext) =>
        throw new NotSupportedException();

    public bool IsSystemAlwaysOnSettingsSupported => false;
    public void OpenSystemAlwaysOnSettings(IUiContext uiContext) =>
        throw new NotSupportedException();

    public bool IsSystemKillSwitchSettingsSupported =>false;
    public void OpenSystemKillSwitchSettings(IUiContext requiredContext) =>
        throw new NotSupportedException();

    public bool IsAppSystemSettingsSupported => false;
    public void OpenAppSystemSettings(IUiContext context) =>
        throw new NotSupportedException();

    public bool IsAppSystemNotificationSettingsSupported => false;
    public void OpenAppSystemNotificationSettings(IUiContext uiContext) =>
        throw new NotSupportedException();
}