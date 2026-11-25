namespace VpnHood.AppLogic.App.Utils;

/// <summary>
/// Provides the debug commands a person can type on the developer page, which the app keeps in the settings
/// (<see cref="VpnHood.AppLogic.Api.Settings.UserSettings.DebugData1"/>).
/// </summary>
public static class DebugCommands
{
    /// <summary>Logs at the debug level.</summary>
    public const string LogDebug = "/log:debug";

    /// <summary>Logs at the trace level.</summary>
    public const string LogTrace = "/log:trace";

    /// <summary>
    /// Connects with a null adapter, which captures nothing, so a test connect leaves the device's network as it is.
    /// </summary>
    public const string NullCapture = "/null-capture";

    /// <summary>Makes the client open a new channel for each connection.</summary>
    public const string NoChannelReuse = "/no-channel-reuse";

    /// <summary>Asks for a review after the next session, as a server's recommendation does.</summary>
    public const string UserReview = "/user-review";

    /// <summary>
    /// Opens remote access for the life of the process, with no pairing, so a UI developer can reach the device.
    /// </summary>
    public const string RemoteAccess = "/remote-access";

    /// <summary>
    /// Makes the client send its session's requests after the hello without WebSocket streams.
    /// </summary>
    public const string DisableWebSocket = "/disable-websocket";

    /// <summary>
    /// Reports the device as a TV (<see cref="VpnHood.AppLogic.Api.App.AppDescriptor.IsTv"/>), so the TV layout can be
    /// worked on from a phone or a desktop.
    /// </summary>
    /// <remarks>
    /// A desktop window follows it as the developer page closes; elsewhere it applies at the next start. Only what
    /// reads <see cref="VpnHood.AppLogic.Api.App.AppDescriptor.IsTv"/> follows it; a platform's own checks still read the
    /// real device.
    /// </remarks>
    public const string TvMode = "/tv-mode";

    /// <summary>
    /// Turns on the Starlink tools in the UI, on Android or in a Debug build; the app itself does not act on it.
    /// </summary>
    public const string Starlink = "/starlink";

    /// <summary>Gets every command, a new array at each read.</summary>
    public static string[] All => [
        LogDebug,
        LogTrace,
        NoChannelReuse,
        NullCapture,
        UserReview,
        RemoteAccess,
        DisableWebSocket,
        TvMode,
        Starlink
    ];
}