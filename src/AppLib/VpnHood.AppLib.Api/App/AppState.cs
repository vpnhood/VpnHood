using VpnHood.AppLib.Api.Billing;
using VpnHood.AppLib.Api.Device;
using VpnHood.AppLib.Api.VpnProfiles;
using VpnHood.AppLib.Api.Proxies;
using VpnHood.AppLib.Api.Sessions;
using VpnHood.AppLib.Api.SplitTunneling;
using VpnHood.AppLib.Api.Updaters;
using VpnHood.Net.Toolkit.ApiClients;

namespace VpnHood.AppLib.Api.App;

/// <summary>Represents the app's state as it stands, which the UI polls to refresh.</summary>
public class AppState
{
    /// <summary>Gets when the app's settings last changed, which tells the UI to read them again.</summary>
    public required DateTime ConfigTime { get; init; }

    /// <summary>Gets when the account last changed, from any UI, or <see langword="null"/> where the build has no accounts (<see cref="AppDescriptor.IsAccountSupported"/>).</summary>
    public required DateTime? AccountTime { get; init; }

    /// <summary>Gets where the VPN's connection stands.</summary>
    public required AppConnectionState ConnectionState { get; init; }

    /// <summary>Gets the session's information, or <see langword="null"/> while the app is idle.</summary>
    public required AppSessionInfo? SessionInfo { get; init; }

    /// <summary>Gets the session's status, or <see langword="null"/> while the app is idle.</summary>
    public required AppSessionStatus? SessionStatus { get; init; }

    /// <summary>Gets the status of the proxies the last connection reached its server through, or <see langword="null"/>.</summary>
    public required AppProxyConnectorStatus? ProxyConnectorStatus { get; init; }

    /// <summary>Gets the session's server location, or, while idle, the one the current VPN profile has chosen, or <see langword="null"/>.</summary>
    public required CurrentServerLocationInfo? ServerLocationInfo { get; init; }

    /// <summary>Gets when the person last asked to connect, or <see langword="null"/>.</summary>
    public required DateTime? ConnectRequestTime { get; init; }

    /// <summary>Gets the last error, until it is cleared, or <see langword="null"/>.</summary>
    public required ApiError? LastError { get; init; }

    /// <summary>Gets the current VPN profile, or <see langword="null"/> where none is chosen.</summary>
    public required VpnProfileBaseInfo? VpnProfile { get; init; }

    /// <summary>Gets a value indicating whether the app is idle: neither connecting, connected nor disconnecting.</summary>
    public required bool IsIdle { get; init; }

    /// <summary>Gets a value indicating whether the UI should offer the log: the app is idle after a diagnose, and a log exists.</summary>
    public required bool PromptForLog { get; init; }

    /// <summary>Gets a value indicating whether the app has a log.</summary>
    public required bool LogExists { get; init; }

    /// <summary>Gets a value indicating whether the last connect was a diagnose, until the last error is cleared.</summary>
    public required bool HasDiagnoseRequested { get; init; }

    /// <summary>Gets a value indicating whether a changed setting needs a reconnect to apply, until the person clears it, connects or disconnects.</summary>
    public required bool IsReconnectRequired { get; init; }

    /// <summary>Gets the updater's status, or <see langword="null"/> where the app has no updater.</summary>
    public required AppUpdaterStatus? UpdaterStatus { get; init; }

    /// <summary>Gets a value indicating whether the UI may offer Disconnect now.</summary>
    public required bool CanDisconnect { get; init; }

    /// <summary>Gets a value indicating whether the UI may offer Connect now.</summary>
    public required bool CanConnect { get; init; }

    /// <summary>Gets a value indicating whether the UI may offer Diagnose now.</summary>
    public required bool CanDiagnose { get; init; }

    /// <summary>Gets the review the server recommends asking for after the last disconnect, or 0 where none is; a recorded review or a new connect sets it back to 0.</summary>
    public required int UserReviewRecommended { get; init; }

    /// <summary>Gets a value indicating whether the UI should offer the Quick Launch, which the person has not been asked about yet.</summary>
    public required bool IsQuickLaunchRecommended { get; init; }

    /// <summary>Gets a value indicating whether the first-run page must ask the person about the anonymous usage data first.</summary>
    public required bool IsAnonymousTrackerPromptRequired { get; init; }

    /// <summary>
    /// Gets the client's country code: the country the server last reported for the client, else the device's region's; or
    /// <see langword="null"/> for a region that is no country, as 419.
    /// </summary>
    public required string? ClientCountryCode { get; init; }

    /// <summary>Gets the language the app's UI uses now.</summary>
    public required UiCultureInfo CurrentUiCultureInfo { get; init; }

    /// <summary>Gets the system's language.</summary>
    public required UiCultureInfo SystemUiCultureInfo { get; init; }

    /// <summary>Gets where the in-app purchase stands, or <see langword="null"/> where the app has no billing.</summary>
    public required PurchaseState? PurchaseState { get; init; }

    /// <summary>Gets the heights of the system bars over the app's window; none where no window is open.</summary>
    public required SystemBarsInfo SystemBarsInfo { get; init; }

    /// <summary>Gets a value indicating whether the app may show notifications, or <see langword="null"/> where the device does not say.</summary>
    public required bool? IsNotificationEnabled { get; init; }

    /// <summary>Gets the device's private DNS, or <see langword="null"/> where the device cannot tell it.</summary>
    public required PrivateDns? SystemPrivateDns { get; init; }

    /// <summary>Gets or sets a value indicating whether the app waits for the UI's own ad to end.</summary>
    public required bool? IsWaitingForInternalAd { get; set; }

    /// <summary>Gets a value indicating whether the privacy settings must offer the ad consent's form again, for the person to change their answer.</summary>
    public required bool IsAdPrivacyOptionsRequired { get; init; }

    /// <summary>Gets the progress of a long connect step, in percent, as the ad's load while the app waits for one, or <see langword="null"/>.</summary>
    public required int? StateProgress { get; init; }

    /// <summary>Gets or sets a value indicating whether a diagnose is under way.</summary>
    public required bool IsDiagnosing { get; set; }

    /// <summary>Gets the protocol of the session's channel, or, while idle, the settings' one.</summary>
    public required ChannelProtocol ChannelProtocol { get; init; }

    /// <summary>Gets a value indicating whether the settings' proxy mode has a proxy to use: the device's proxy where it has one, or the person's list where it is not empty.</summary>
    public required bool IsProxyEndPointActive { get; init; }

    /// <summary>Gets a value indicating whether the app has a promotion to show: its image is saved, the remote settings' promotion is on, and the current VPN profile is not premium; never in China.</summary>
    public required bool PromotionExists { get; init; }

    /// <summary>Gets why the TCP forwarding switch is locked, if it is.</summary>
    public required TcpForwardingLockReason TcpForwardingLockReason { get; init; }

    /// <summary>Gets the split tunneling's state, as the settings and the session make it.</summary>
    public required SplitTunnelingState SplitTunnelingState { get; init; }

    /// <summary>Gets the always-on state, or <see langword="null"/> where the app does not keep always-on itself (<see cref="AppDescriptor.AlwaysOnSupport"/>).</summary>
    public required AlwaysOnState? AlwaysOnState { get; init; }

    /// <summary>Gets a value indicating whether the app's kill switch blocks the internet with no session, but for the device-level split.</summary>
    /// <remarks>
    /// The person's disconnect lifts it, which the UI offers beside Connect, as <see cref="CanDisconnect"/> is
    /// <see langword="false"/> with the session gone; but where it is kept while disconnected
    /// (<see cref="VpnHood.AppLib.Api.Settings.KillSwitchMode.Lockdown"/>), only another level does.
    /// </remarks>
    public required bool IsKillSwitchBlocking { get; init; }
}