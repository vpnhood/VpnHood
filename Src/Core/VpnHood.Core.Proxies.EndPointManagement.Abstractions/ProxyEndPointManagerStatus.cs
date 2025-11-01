namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions;

public class ProxyEndPointManagerStatus
{
    public ProxyEndPointInfo[] ProxyEndPointInfos { get; init; } = [];
    public bool IsAnySucceeded { get; init; }
    public bool AutoUpdate { get; init; }
}