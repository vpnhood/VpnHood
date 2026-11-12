namespace VpnHood.AppLib.Api.Proxies;

/// <summary>
/// Represents the tally of proxy connections in the current VPN session, across every proxy, as it stood when it was
/// read.
/// </summary>
/// <remarks>A proxy's own rating is in <see cref="ProxyEndPointStatus"/>.</remarks>
public class ProxySessionStatus
{
    /// <summary>Gets or sets the number of connections that succeeded.</summary>
    public int SucceededCount { get; set; }

    /// <summary>Gets or sets the number of connections that failed.</summary>
    public int FailedCount { get; set; }

    /// <summary>Gets or sets how long the last connection took, or <see langword="null"/> when it failed.</summary>
    public TimeSpan? Latency { get; set; }

    /// <summary>Gets or sets when a connection last succeeded, in UTC, or <see langword="null"/>.</summary>
    public DateTime? LastSucceeded { get; set; }

    /// <summary>Gets or sets when a connection last failed, in UTC, or <see langword="null"/>.</summary>
    public DateTime? LastFailed { get; set; }

    /// <summary>Gets or sets the last failure's message, or <see langword="null"/> after a success.</summary>
    public string? ErrorMessage { get; set; }
}
