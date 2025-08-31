namespace VpnHood.Core.Client.ProxyServers;

public class ProxyServerStatus
{
    public bool IsActive { get; set; } = true;
    public int Penalty { get; set; }
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }
    public TimeSpan Latency { get; set; }
    public DateTime ConnectionTime { get; set; }
}