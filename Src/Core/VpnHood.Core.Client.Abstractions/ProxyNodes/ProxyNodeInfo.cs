namespace VpnHood.Core.Client.Abstractions.ProxyNodes;

public class ProxyNodeInfo(ProxyNode node)
{
    public ProxyNode Node { get; set; } = node;
    public ProxyNodeStatus Status { get; set; } = new();
}