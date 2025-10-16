namespace VpnHood.Core.Client.Abstractions.ProxyNodes;

public class ProxyNodeInfo
{
    public required ProxyNode Node { get; set; }
    public ProxyNodeStatus Status { get; set; } = new();
}