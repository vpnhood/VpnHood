using VpnHood.Core.Proxies.EndPointManagement.Abstractions;

namespace VpnHood.AppLib.Dtos;

public class AppProxyEndPointManagerStatus
{
    public required ProxyEndPointStatus SessionStatus { get; init; }
    public required int SucceededServerCount { get; init; }
    public required int FailedServerCount { get; init; }
    public required int UnknownServerCount { get; init; }
}