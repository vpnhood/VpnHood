namespace VpnHood.AppLib.Api.Proxies;

// How one proxy has been behaving. Quality is a verdict, not a field a UI derives: the engine ranks
// on penalty and attempt counts, and a second implementation of that rule in a UI would drift from
// the one the engine actually routes by.

/// <summary>
/// Represents a proxy endpoint's record of use, which rates it and sets its turn in the rotation.
/// </summary>
public class ProxyEndPointStatus
{
    /// <summary>
    /// Gets or sets the endpoint's penalty: a managed connector adds 2 for each failure and 1 for each success much
    /// slower than the fastest endpoint's, and takes 1 off for each other success, down to 0.
    /// </summary>
    public int Penalty { get; set; }

    /// <summary>Gets or sets the number of successes.</summary>
    public int SucceededCount { get; set; }

    /// <summary>Gets or sets the number of failures.</summary>
    public int FailedCount { get; set; }

    /// <summary>Gets or sets how long the last success took, or <see langword="null"/>.</summary>
    public TimeSpan? Latency { get; set; }

    /// <summary>Gets or sets when the endpoint last succeeded, in UTC, or <see langword="null"/>.</summary>
    public DateTime? LastSucceeded { get; set; }

    /// <summary>Gets or sets when the endpoint last failed, in UTC, or <see langword="null"/>.</summary>
    public DateTime? LastFailed { get; set; }

    /// <summary>
    /// Gets or sets the last failure's message, or why the endpoint was disabled as a duplicate; <see langword="null"/>
    /// after a success.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the endpoint's place in the rotation: after each use, the connector's queue position then, plus 3
    /// times the penalty, plus 1; the endpoint whose place is lowest against the current position comes first.
    /// </summary>
    public long QueuePosition { get; set; }

    /// <summary>Gets the endpoint's quality, from its penalty and its successes.</summary>
    public StatusQuality Quality { get; set; }
}
