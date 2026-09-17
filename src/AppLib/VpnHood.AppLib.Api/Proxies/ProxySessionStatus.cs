namespace VpnHood.AppLib.Api.Proxies;

// Proxy connect attempts during the current VPN session, across all endpoints - so no rating
// fields, which belong to one endpoint (ProxyEndPointStatus).
public class ProxySessionStatus
{
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }
    public TimeSpan? Latency { get; set; }
    public DateTime? LastSucceeded { get; set; }
    public DateTime? LastFailed { get; set; }
    public string? ErrorMessage { get; set; }
}
