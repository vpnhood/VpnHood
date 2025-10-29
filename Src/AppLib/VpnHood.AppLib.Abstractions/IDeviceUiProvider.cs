using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IDeviceUiProvider
{
    bool IsQuickLaunchSupported { get; }
    bool IsRequestQuickLaunchSupported { get; }
    Task<bool> RequestQuickLaunch(IUiContext uiContext, CancellationToken cancellationToken);

    bool? IsNotificationEnabled { get; }
    bool IsRequestNotificationSupported { get; }
    Task<bool> RequestNotification(IUiContext uiContext, CancellationToken cancellationToken);

    bool IsPrivateDnsSettingsSupported { get; }

    bool IsSettingsSupported { get; }
    void OpenSettings(IUiContext uiContext);

    bool IsAlwaysOnSettingsSupported { get; }
    void OpenAlwaysOnSettings(IUiContext uiContext);

    bool IsKillSwitchSettingsSupported { get; }
    void OpenKillSwitchSettings(IUiContext requiredContext);

    bool IsAppSettingsSupported { get; }
    void OpenAppSettings(IUiContext context);

    bool IsAppNotificationSettingsSupported { get; }
    void OpenAppNotificationSettings(IUiContext uiContext);

    bool IsProxySettingsSupported { get; }
    DeviceProxySettings? GetProxySettings();

    PrivateDns? GetPrivateDns();
    SystemBarsInfo GetBarsInfo(IUiContext uiContext);
}