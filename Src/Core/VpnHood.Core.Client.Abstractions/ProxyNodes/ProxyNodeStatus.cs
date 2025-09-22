namespace VpnHood.Core.Client.Abstractions.ProxyNodes;

public class ProxyNodeStatus
{
    public int Penalty { get; set; }
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }
    public TimeSpan? Latency { get; set; }
    public DateTime? LastUsedTime { get; set; }
    public string? ErrorMessage { get; set; }
}