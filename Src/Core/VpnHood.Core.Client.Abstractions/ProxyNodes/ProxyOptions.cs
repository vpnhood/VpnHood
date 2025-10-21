namespace VpnHood.Core.Client.Abstractions.ProxyNodes;

public class ProxyOptions
{
    public ProxyNode[] ProxyNodes { get; init; } = [];
    public Uri? AutoUpdateListUrl { get; set; }
    public TimeSpan? AutoUpdateInterval { get; set; }
    public int AutoUpdateMinPenalty { get; set; }
    public bool ResetStates { get; set; }
}