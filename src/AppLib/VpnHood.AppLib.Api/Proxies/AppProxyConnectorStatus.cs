namespace VpnHood.AppLib.Api.Proxies;

/// <summary>
/// Represents a proxy connector's state, as it stood when it was read: its proxies by the outcome of their last use,
/// and its session's tally.
/// </summary>
public class AppProxyConnectorStatus
{
    /// <summary>Gets the tally of the connector's connections in this session.</summary>
    public required ProxySessionStatus SessionStatus { get; init; }

    /// <summary>Gets the number of enabled endpoints whose last use succeeded.</summary>
    public required int SucceededServerCount { get; init; }

    /// <summary>Gets the number of enabled endpoints used whose last use did not succeed.</summary>
    public required int FailedServerCount { get; init; }

    /// <summary>Gets the number of enabled endpoints never used.</summary>
    public required int UnknownServerCount { get; init; }

    /// <summary>Gets the number of disabled endpoints.</summary>
    public required int DisabledServerCount { get; init; }
}