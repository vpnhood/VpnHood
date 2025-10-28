namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;

public class ProxyAutoUpdateOptions
{
    public Uri? Url { get; set; }
    public TimeSpan? Interval { get; set; }
    public int MaxPenalty { get; set; } = 50;
    public int MaxItemCount { get; set; } = 400;
}