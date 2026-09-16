
namespace VpnHood.AppLib.Contracts.App;

// What the device can open or ask for, read off the providers once by the app and carried as
// plain values: the same object is deserialized by a UI on the other side of the API, which
// has no providers to ask.
public class DeviceIntentFeatures
{
    public required bool IsUserReviewSupported { get; init; }

    // Whether a link may be handed to an external browser. The SPA can always render a page inside
    // itself, so this gates only what LEAVES the app — the account website, the web purchase page.
    // Not the same question as IsTv: a television with a browser installed can open both.
    public required bool IsWebBrowserSupported { get; init; }

    public required bool IsQuickLaunchSupported { get; init; }
    public required bool IsRequestQuickLaunchSupported { get; init; }
    public required bool IsRequestNotificationSupported { get; init; }
    public required bool IsPrivateDnsSettingsSupported { get; init; }
    public required bool IsKillSwitchSettingsSupported { get; init; }
    public required bool IsAlwaysOnSettingsSupported { get; init; }
    public required bool IsSettingsSupported { get; init; }
    public required bool IsAppSettingsSupported { get; init; }
    public required bool IsAppNotificationSettingsSupported { get; init; }
}
