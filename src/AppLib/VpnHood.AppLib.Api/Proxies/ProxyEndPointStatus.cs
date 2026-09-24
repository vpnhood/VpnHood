namespace VpnHood.AppLib.Api.Proxies;

// How one proxy has been behaving. Quality is a verdict, not a field a UI derives: the engine ranks
// on penalty and attempt counts, and a second implementation of that rule in a UI would drift from
// the one the engine actually routes by.
public class ProxyEndPointStatus
{
    public int Penalty { get; set; }
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }
    public TimeSpan? Latency { get; set; }
    public DateTime? LastSucceeded { get; set; }
    public DateTime? LastFailed { get; set; }
    public string? ErrorMessage { get; set; }
    public long QueuePosition { get; set; }
    public StatusQuality Quality { get; set; }
}
