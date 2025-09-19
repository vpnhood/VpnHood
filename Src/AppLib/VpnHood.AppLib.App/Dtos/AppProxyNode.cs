using VpnHood.Core.Client.Abstractions.ProxyNodes;

namespace VpnHood.AppLib.Dtos;

public class AppProxyNode
{
    public required ProxyNode Node { get; init; }
    public required ProxyNodeStatus Status { get; init; }
}