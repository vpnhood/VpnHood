namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;

public class ProxyAutoUpdateOptions
{
    public Uri? Url { get; set; }
    public TimeSpan? Interval { get; set; }
    public int MinPenalty { get; set; }
    public int MaxItemCount { get; set; }
}