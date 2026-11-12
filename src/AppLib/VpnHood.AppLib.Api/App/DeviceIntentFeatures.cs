
namespace VpnHood.AppLib.Api.App;

/// <summary>
/// Represents what the UI may ask of the device, read off the device once and carried as plain values, as a UI across
/// the API has no device to ask.
/// </summary>
public class DeviceIntentFeatures
{
    /// <summary>Gets a value indicating whether a store's review can be asked for.</summary>
    public required bool IsUserReviewSupported { get; init; }

    /// <summary>Gets a value indicating whether a link may be handed to an external browser, as the account website or a web purchase page.</summary>
    public required bool IsWebBrowserSupported { get; init; }

    /// <summary>Gets a value indicating whether the device offers the app's Quick Launch.</summary>
    public required bool IsQuickLaunchSupported { get; init; }

    /// <summary>Gets a value indicating whether the app can ask the system to add its Quick Launch.</summary>
    public required bool IsRequestQuickLaunchSupported { get; init; }

    /// <summary>Gets a value indicating whether the app can ask the person to let it show notifications.</summary>
    public required bool IsRequestNotificationSupported { get; init; }

    /// <summary>Gets a value indicating whether the device has a private DNS setting, which can take the DNS past the VPN.</summary>
    public required bool IsPrivateDnsSettingsSupported { get; init; }

    /// <summary>Gets a value indicating whether the app can open the system's kill switch setting.</summary>
    public required bool IsKillSwitchSettingsSupported { get; init; }

    /// <summary>Gets a value indicating whether the app can open the system's settings.</summary>
    public required bool IsSettingsSupported { get; init; }

    /// <summary>Gets a value indicating whether the app can open its own page in the system's settings.</summary>
    public required bool IsAppSettingsSupported { get; init; }

    /// <summary>Gets a value indicating whether the app can open its notification settings in the system's settings.</summary>
    public required bool IsAppNotificationSettingsSupported { get; init; }

    /// <summary>Gets a value indicating whether the device has a share sheet.</summary>
    public required bool IsShareSupported { get; init; }
}
