using System.Text.Json.Serialization;

namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>
/// Live tally of proxy connect attempts during the current VPN session. Unlike
/// <see cref="ProxyEndPointStatus"/> it is not tied to a single endpoint, so it carries
/// no rating fields (Penalty, QueuePosition, Quality).
/// </summary>
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

    /// <summary>Gets a value indicating whether the last connection succeeded.</summary>
    [JsonIgnore]
    public bool IsLastUsedSucceeded =>
        LastSucceeded != null && (LastFailed is null || LastSucceeded > LastFailed);

    /// <summary>
    /// Gets a value indicating whether the last connection failed: a failure at the same moment as the last success
    /// too.
    /// </summary>
    [JsonIgnore]
    public bool IsLastUsedFailed =>
        LastFailed != null && (LastSucceeded is null || LastFailed >= LastSucceeded);

    /// <summary>
    /// Gets the later of <see cref="LastSucceeded"/> and <see cref="LastFailed"/>, or <see langword="null"/> while no
    /// connection has been counted.
    /// </summary>
    [JsonIgnore]
    public DateTime? LastUsed =>
        LastFailed is null || LastSucceeded > LastFailed ? LastSucceeded : LastFailed;

    /// <summary>Gets a value indicating whether any connection has been counted.</summary>
    [JsonIgnore]
    public bool HasUsed => SucceededCount > 0 || FailedCount > 0;
}
