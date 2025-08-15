using VpnHood.Core.Client.Device.UiContexts;

namespace VpnHood.AppLib.Abstractions;

public interface IAppUiProvider
{
    bool IsRequestQuickLaunchSupported { get; }
    Task<bool> RequestQuickLaunch(IUiContext uiContext, CancellationToken cancellationToken);

    bool IsRequestNotificationSupported { get; }
    Task<bool> RequestNotification(IUiContext uiContext, CancellationToken cancellationToken);

    bool IsAlwaysOnSupported { get; }
    void OpenSystemAlwaysOn(IUiContext uiContext);

    bool IsSystemSettingsSupported { get; }
    void OpenSystemSettings(IUiContext uiContext);

    bool IsAppSystemSettingsSupported { get; }
    void OpenAppSystemSettings(IUiContext context);

    bool IsAppSystemNotificationSettingsSupported { get; }
    void OpenAppSystemNotificationSettings(IUiContext uiContext);


    SystemBarsInfo GetSystemBarsInfo(IUiContext uiContext);
}