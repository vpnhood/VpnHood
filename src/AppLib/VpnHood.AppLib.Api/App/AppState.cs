using VpnHood.AppLib.Api.Billing;
using VpnHood.AppLib.Api.Device;
using VpnHood.AppLib.Api.VpnProfiles;
using VpnHood.AppLib.Api.Proxies;
using VpnHood.AppLib.Api.Sessions;
using VpnHood.AppLib.Api.SplitTunneling;
using VpnHood.AppLib.Api.Updaters;
using VpnHood.Net.Toolkit.ApiClients;

namespace VpnHood.AppLib.Api.App;

public class AppState
{
    public required DateTime ConfigTime { get; init; }
    public required AppConnectionState ConnectionState { get; init; }
    public required AppSessionInfo? SessionInfo { get; init; }
    public required AppSessionStatus? SessionStatus { get; init; }
    public required AppProxyConnectorStatus? ProxyConnectorStatus { get; init; }
    public required CurrentServerLocationInfo? ServerLocationInfo { get; init; }
    public required DateTime? ConnectRequestTime { get; init; }
    public required ApiError? LastError { get; init; }
    public required VpnProfileBaseInfo? VpnProfile { get; init; }
    public required bool IsIdle { get; init; }
    public required bool PromptForLog { get; init; }
    public required bool LogExists { get; init; }
    public required bool HasDiagnoseRequested { get; init; }
    public required bool IsReconnectRequired { get; init; }
    public required AppUpdaterStatus? UpdaterStatus { get; init; }
    public required bool CanDisconnect { get; init; }
    public required bool CanConnect { get; init; }
    public required bool CanDiagnose { get; init; }
    public required int UserReviewRecommended { get; init; }
    public required bool IsQuickLaunchRecommended { get; init; }
    public required bool IsAnonymousTrackerPromptRequired { get; init; } // the first-run page asks for usage data first
    public required string? ClientCountryCode { get; init; } // null for a region that is no country (419)
    public required UiCultureInfo CurrentUiCultureInfo { get; init; }
    public required UiCultureInfo SystemUiCultureInfo { get; init; }
    public required PurchaseState? PurchaseState { get; init; }
    public required SystemBarsInfo SystemBarsInfo { get; init; }
    public required bool? IsNotificationEnabled { get; init; }
    public required PrivateDns? SystemPrivateDns { get; init; }
    public required bool? IsWaitingForInternalAd { get; set; }
    public required bool IsAdPrivacyOptionsRequired { get; init; } // the ad consent can be changed in the privacy settings
    public required int? StateProgress { get; init; }
    public required bool IsDiagnosing { get; set; }
    public required ChannelProtocol ChannelProtocol { get; init; }
    public required bool IsProxyEndPointActive { get; init; }
    public required bool PromotionExists { get; init; }
    public required TcpProxyUsageReason TcpProxyUsageReason { get; init; }
    public required SplitTunnelingState SplitTunnelingState { get; init; }

    // null where the app does not keep always on itself (AppDescriptor.AlwaysOnSupport)
    public required AlwaysOnState? AlwaysOnState { get; init; }

    // This app's kill switch block is up with no session: the internet is blocked but for the device-level split.
    // A person's disconnect lifts it, which a UI offers beside Connect (CanDisconnect is false, as the session
    // is gone), but where it is kept while disconnected (UserSettings.KillSwitchLockdown): then only turning
    // that or the switch off does.
    public required bool IsKillSwitchBlocking { get; init; }
}