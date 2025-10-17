using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.WebServer.Api;

public class AppIntentFeatures(
    IDeviceUiProvider? uiProvider,
    IAppUserReviewProvider? userReviewProvider)
{
    public bool IsUserReviewSupported => userReviewProvider != null;
    public bool IsQuickLaunchSupported => uiProvider?.IsQuickLaunchSupported ?? false;
    public bool IsRequestQuickLaunchSupported => uiProvider?.IsRequestQuickLaunchSupported ?? false;
    public bool IsRequestNotificationSupported => uiProvider?.IsRequestNotificationSupported ?? false;
    public bool IsSystemPrivateDnsSettingsSupported => uiProvider?.IsSystemPrivateDnsSettingsSupported ?? false;
    public bool IsSystemKillSwitchSettingsSupported => uiProvider?.IsSystemKillSwitchSettingsSupported ?? false;
    public bool IsSystemAlwaysOnSettingsSupported => uiProvider?.IsSystemAlwaysOnSettingsSupported ?? false;
    public bool IsSystemSettingsSupported => uiProvider?.IsSystemSettingsSupported ?? false;
    public bool IsAppSystemSettingsSupported => uiProvider?.IsAppSystemSettingsSupported ?? false;
    public bool IsAppSystemNotificationSettingsSupported => uiProvider?.IsAppSystemNotificationSettingsSupported ?? false;
}