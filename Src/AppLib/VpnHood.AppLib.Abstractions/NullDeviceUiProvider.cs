using System.Net;
using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public class NullDeviceUiProvider : IDeviceUiProvider
{
    public virtual PrivateDns? GetSystemPrivateDns() => null;
    public virtual SystemBarsInfo GetSystemBarsInfo(IUiContext uiContext) => SystemBarsInfo.Default;

    public virtual bool IsQuickLaunchSupported => false;
    public virtual bool IsRequestQuickLaunchSupported => false;

    public virtual Task<bool> RequestQuickLaunch(IUiContext uiContext, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public virtual bool? IsNotificationEnabled => false;

    public virtual bool IsRequestNotificationSupported => false;

    public virtual Task<bool> RequestNotification(IUiContext uiContext, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public virtual bool IsSystemPrivateDnsSettingsSupported => false;

    public virtual bool IsSystemSettingsSupported => false;
    public virtual void OpenSystemSettings(IUiContext uiContext) =>
        throw new NotSupportedException();

    public virtual bool IsSystemAlwaysOnSettingsSupported => false;
    public virtual void OpenSystemAlwaysOnSettings(IUiContext uiContext) =>
        throw new NotSupportedException();

    public virtual bool IsSystemKillSwitchSettingsSupported =>false;
    public virtual void OpenSystemKillSwitchSettings(IUiContext requiredContext) =>
        throw new NotSupportedException();

    public virtual bool IsAppSystemSettingsSupported => false;
    public virtual void OpenAppSystemSettings(IUiContext context) =>
        throw new NotSupportedException();

    public virtual bool IsAppSystemNotificationSettingsSupported => false;
    public virtual void OpenAppSystemNotificationSettings(IUiContext uiContext) =>
        throw new NotSupportedException();

    public virtual bool IsProxySettingsSupported => false;
    public virtual DeviceProxySettings? GetProxySettings() => null;
}