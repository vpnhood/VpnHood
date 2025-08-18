namespace VpnHood.AppLib.WebServer.Api;

public class IntentStatus
{
    public required bool IsRequestQuickLaunchSupported { get; init; }
    public required bool IsRequestNotificationSupported { get; init; }
    public required bool? IsNotificationEnabled { get; init; }
    public required bool IsUserReviewSupported { get; init; }
    public required bool IsSystemPrivateDnsSettingsSupported { get; init; }
    public required bool IsSystemKillSwitchSettingsSupported { get; init; }
    public required bool IsSystemAlwaysOnSettingsSupported { get; init; }
    public required bool IsSystemSettingsSupported { get; init; }
    public required bool IsAppSystemSettingsSupported { get; init; }
    public required bool IsAppSystemNotificationSettingsSupported { get; init; }
}