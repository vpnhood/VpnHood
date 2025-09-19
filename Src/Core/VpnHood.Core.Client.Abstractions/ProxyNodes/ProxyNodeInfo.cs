namespace VpnHood.Core.Client.Abstractions.ProxyNodes;

public class ProxyNodeInfo(ProxyNode node)
{
    public ProxyNode Node => node;
    public ProxyNodeStatus Status { get; init; } = new();
}