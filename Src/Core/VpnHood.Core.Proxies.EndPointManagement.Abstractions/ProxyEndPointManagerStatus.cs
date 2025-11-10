namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions;

public class ProxyEndPointManagerStatus
{
    public required ProxyEndPointInfo[] ProxyEndPointInfos { get; init; } = [];
    public required bool IsAnySucceeded { get; init; }
    public required bool AutoUpdate { get; init; }
    public required ProxyEndPointStatus SessionStatus { get; init; }
}