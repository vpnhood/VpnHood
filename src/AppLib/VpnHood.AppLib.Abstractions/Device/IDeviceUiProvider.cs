using VpnHood.Core.Client.Devices.UiContexts;

namespace VpnHood.AppLib.Abstractions.Device;

public interface IDeviceUiProvider
{
    bool IsQuickLaunchSupported { get; }
    bool IsRequestQuickLaunchSupported { get; }
    Task<bool> RequestQuickLaunch(IUiContext uiContext, CancellationToken cancellationToken);

    bool? IsNotificationEnabled { get; }
    bool IsRequestNotificationSupported { get; }
    Task<bool> RequestNotification(IUiContext uiContext, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a page can be handed to an EXTERNAL browser. Not "is there a web view" — the SPA can
    /// always render a page inside itself; this is only about leaving the app. False on a device
    /// with no browser, which is where an outbound link opens nothing at all: the UI withholds the
    /// link rather than offering a dead one.
    /// </summary>
    bool IsWebBrowserSupported { get; }

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