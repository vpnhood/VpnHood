using System.Text.Json.Serialization;

namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions;

/// <summary>
/// Live tally of proxy connect attempts during the current VPN session. Unlike
/// <see cref="ProxyEndPointStatus"/> it is not tied to a single endpoint, so it carries
/// no rating fields (Penalty, QueuePosition, Quality).
/// </summary>
public class ProxySessionStatus
{
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }
    public TimeSpan? Latency { get; set; }
    public DateTime? LastSucceeded { get; set; }
    public DateTime? LastFailed { get; set; }
    public string? ErrorMessage { get; set; }

    [JsonIgnore]
    public bool IsLastUsedSucceeded =>
        LastSucceeded != null && (LastFailed is null || LastSucceeded > LastFailed);

    [JsonIgnore]
    public bool IsLastUsedFailed =>
        LastFailed != null && (LastSucceeded is null || LastFailed > LastSucceeded);

    [JsonIgnore]
    public DateTime? LastUsed =>
        LastSucceeded > LastFailed ? LastSucceeded : LastFailed;

    [JsonIgnore]
    public bool HasUsed => SucceededCount > 0 || FailedCount > 0;
}
