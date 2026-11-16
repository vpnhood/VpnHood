using System.Text.Json.Serialization;

namespace VpnHood.Core.Proxies.Management.Abstractions;

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

    /// <summary>Gets a value indicating whether the endpoint's last use succeeded.</summary>
    [JsonIgnore]
    public bool IsLastUsedSucceeded =>
        LastSucceeded != null && (LastFailed is null || LastSucceeded > LastFailed);

    /// <summary>Gets a value indicating whether the endpoint's last use failed.</summary>
    [JsonIgnore]
    public bool IsLastUsedFailed =>
        LastFailed != null && (LastSucceeded is null || LastFailed > LastSucceeded);

    /// <summary>
    /// Gets <see cref="LastSucceeded"/> when it is later than <see cref="LastFailed"/>, and otherwise
    /// <see cref="LastFailed"/>, which is <see langword="null"/> while the endpoint has never failed, whatever it
    /// succeeded.
    /// </summary>
    [JsonIgnore]
    public DateTime? LastUsed =>
        LastSucceeded > LastFailed ? LastSucceeded : LastFailed;

    /// <summary>Gets a value indicating whether the endpoint has been used.</summary>
    [JsonIgnore]
    public bool HasUsed => SucceededCount > 0 || FailedCount > 0;

    /// <summary>Gets the endpoint's quality, from its penalty and its successes.</summary>
    public StatusQuality Quality {
        get {
            return Penalty switch {
                <= 0 when SucceededCount is 0 && FailedCount is 0 => StatusQuality.Unknown,
                <= 0 when SucceededCount > 0 => StatusQuality.Excellent,
                <= 10 when SucceededCount > 0 => StatusQuality.Good,
                <= 20 when SucceededCount > 0 => StatusQuality.Fair,
                <= 100 when SucceededCount > 0 => StatusQuality.Poor,
                <= 10000 when SucceededCount > 0 => StatusQuality.VeryPoor,
                _ => StatusQuality.Failed
            };
        }
    }
}