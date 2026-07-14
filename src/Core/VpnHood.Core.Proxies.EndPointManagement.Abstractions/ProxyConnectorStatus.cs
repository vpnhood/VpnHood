namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions;

public class ProxyConnectorStatus
{
    public required bool IsAnySucceeded { get; init; }
    public required bool AutoUpdate { get; init; }
    public required ProxySessionStatus SessionStatus { get; init; }
    public required int SucceededServerCount { get; init; }
    public required int FailedServerCount { get; init; }
    public required int UnknownServerCount { get; init; }
    public required int DisabledServerCount { get; set; }
}