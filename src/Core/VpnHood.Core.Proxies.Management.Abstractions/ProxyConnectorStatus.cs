namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>
/// Represents a proxy connector's state: its endpoints by the outcome of their last use, and its session's tally.
/// </summary>
public class ProxyConnectorStatus
{
    /// <summary>
    /// Gets a value indicating whether any endpoint has no error recorded: one that succeeded last, or one never
    /// used. The client gives up on its proxies while it is <see langword="false"/>.
    /// </summary>
    public required bool IsAnySucceeded { get; init; }

    /// <summary>Gets a value indicating whether the endpoint list updates itself from a URL.</summary>
    public required bool AutoUpdate { get; init; }

    /// <summary>
    /// Gets the tally of the connector's connections in this session: the connector's own object, which later
    /// connections update.
    /// </summary>
    public required ProxySessionStatus SessionStatus { get; init; }

    /// <summary>Gets the number of enabled endpoints whose last use succeeded.</summary>
    public required int SucceededServerCount { get; init; }

    /// <summary>Gets the number of enabled endpoints used whose last use did not succeed.</summary>
    public required int FailedServerCount { get; init; }

    /// <summary>Gets the number of enabled endpoints never used.</summary>
    public required int UnknownServerCount { get; init; }

    /// <summary>Gets or sets the number of disabled endpoints.</summary>
    public required int DisabledServerCount { get; set; }
}