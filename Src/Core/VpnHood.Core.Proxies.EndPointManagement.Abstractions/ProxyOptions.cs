namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions;

public class ProxyOptions
{
    public ProxyEndPoint[] ProxyEndPoints { get; init; } = [];
    public Uri? AutoUpdateListUrl { get; set; }
    public TimeSpan? AutoUpdateInterval { get; set; }
    public int AutoUpdateMinPenalty { get; set; }
    public bool ResetStates { get; set; }
}