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
    public bool IsPrivateDnsSettingsSupported => uiProvider?.IsPrivateDnsSettingsSupported ?? false;
    public bool IsKillSwitchSettingsSupported => uiProvider?.IsKillSwitchSettingsSupported ?? false;
    public bool IsAlwaysOnSettingsSupported => uiProvider?.IsAlwaysOnSettingsSupported ?? false;
    public bool IsSettingsSupported => uiProvider?.IsSettingsSupported ?? false;
    public bool IsAppSettingsSupported => uiProvider?.IsAppSettingsSupported ?? false;
    public bool IsAppNotificationSettingsSupported => uiProvider?.IsAppNotificationSettingsSupported ?? false;
}