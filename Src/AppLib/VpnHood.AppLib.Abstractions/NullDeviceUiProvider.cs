using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public class NullDeviceUiProvider : IDeviceUiProvider
{
    public virtual PrivateDns? GetPrivateDns() => null;
    public virtual SystemBarsInfo GetBarsInfo(IUiContext uiContext) => SystemBarsInfo.Default;

    public virtual bool IsQuickLaunchSupported => false;
    public virtual bool IsRequestQuickLaunchSupported => false;

    public virtual Task<bool> RequestQuickLaunch(IUiContext uiContext, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public virtual bool? IsNotificationEnabled => false;

    public virtual bool IsRequestNotificationSupported => false;

    public virtual Task<bool> RequestNotification(IUiContext uiContext, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public virtual bool IsPrivateDnsSettingsSupported => false;

    public virtual bool IsSettingsSupported => false;
    public virtual void OpenSettings(IUiContext uiContext) =>
        throw new NotSupportedException();

    public virtual bool IsAlwaysOnSettingsSupported => false;
    public virtual void OpenAlwaysOnSettings(IUiContext uiContext) =>
        throw new NotSupportedException();

    public virtual bool IsKillSwitchSettingsSupported =>false;
    public virtual void OpenKillSwitchSettings(IUiContext requiredContext) =>
        throw new NotSupportedException();

    public virtual bool IsAppSettingsSupported => false;
    public virtual void OpenAppSettings(IUiContext context) =>
        throw new NotSupportedException();

    public virtual bool IsAppNotificationSettingsSupported => false;
    public virtual void OpenAppNotificationSettings(IUiContext uiContext) =>
        throw new NotSupportedException();

    public virtual bool IsProxySettingsSupported => false;
    public virtual DeviceProxySettings? GetProxySettings() => null;
}