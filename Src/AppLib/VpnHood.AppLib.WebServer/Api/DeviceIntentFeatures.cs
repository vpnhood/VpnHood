using VpnHood.AppLib.Abstractions;

namespace VpnHood.AppLib.WebServer.Api;

public class DeviceIntentFeatures(
    IDeviceUiProvider? uiProvider,
    IAppUserReviewProvider? userReviewProvider)
{
    public bool IsUserReviewSupported => userReviewProvider != null;
    public bool IsQuickLaunchSupported => uiProvider?.IsQuickLaunchSupported ?? false;
    public bool IsRequestQuickLaunchSupported => uiProvider?.IsRequestQuickLaunchSupported ?? false;
    public bool IsRequestNotificationSupported => uiProvider?.IsRequestNotificationSupported ?? false;
    public bool IsSystemPrivateDnsSettingsSupported => uiProvider?.IsPrivateDnsSettingsSupported ?? false;
    public bool IsSystemKillSwitchSettingsSupported => uiProvider?.IsKillSwitchSettingsSupported ?? false;
    public bool IsSystemAlwaysOnSettingsSupported => uiProvider?.IsAlwaysOnSettingsSupported ?? false;
    public bool IsSystemSettingsSupported => uiProvider?.IsSettingsSupported ?? false;
    public bool IsAppSystemSettingsSupported => uiProvider?.IsAppSettingsSupported ?? false;
    public bool IsAppSystemNotificationSettingsSupported => uiProvider?.IsAppNotificationSettingsSupported ?? false;
}