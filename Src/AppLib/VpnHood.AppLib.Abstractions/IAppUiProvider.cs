using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IAppUiProvider
{
    bool IsRequestQuickLaunchSupported { get; }
    Task<bool> RequestQuickLaunch(IUiContext uiContext, CancellationToken cancellationToken);

    bool? IsNotificationEnabled { get; }
    bool IsRequestNotificationSupported { get; }
    Task<bool> RequestNotification(IUiContext uiContext, CancellationToken cancellationToken);

    bool IsSystemPrivateDnsSettingsSupported { get; }

    bool IsSystemSettingsSupported { get; }
    void OpenSystemSettings(IUiContext uiContext);

    bool IsSystemAlwaysOnSettingsSupported { get; }
    void OpenSystemAlwaysOnSettings(IUiContext uiContext);

    bool IsSystemKillSwitchSettingsSupported { get; }
    void OpenSystemKillSwitchSettings(IUiContext requiredContext);

    bool IsAppSystemSettingsSupported { get; }
    void OpenAppSystemSettings(IUiContext context);

    bool IsAppSystemNotificationSettingsSupported { get; }
    void OpenAppSystemNotificationSettings(IUiContext uiContext);


    SystemBarsInfo GetSystemBarsInfo(IUiContext uiContext);
}