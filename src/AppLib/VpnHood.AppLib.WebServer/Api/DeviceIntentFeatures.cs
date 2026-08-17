using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Abstractions.Device;

namespace VpnHood.AppLib.WebServer.Api;

public class DeviceIntentFeatures(
    IDeviceUiProvider? uiProvider,
    IAppUserReviewProvider? userReviewProvider)
{
    public bool IsUserReviewSupported => userReviewProvider != null;

    // Whether a link may be handed to an external browser. The SPA can always render a page inside
    // itself, so this gates only what LEAVES the app — the account website, the web purchase page.
    // Not the same question as IsTv: a television with a browser installed can open both.
    public bool IsWebBrowserSupported => uiProvider?.IsWebBrowserSupported ?? false;


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