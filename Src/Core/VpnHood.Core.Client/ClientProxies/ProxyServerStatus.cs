namespace VpnHood.Core.Client.ClientProxies;

public class ProxyServerStatus
{
    public ProxyServerState State { get; set; }
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }
    public int TotalFailedCount { get; set; }
    public TimeSpan LastResponseDuration { get; set; }
    public DateTime LastConnectionTime { get; set; }
}