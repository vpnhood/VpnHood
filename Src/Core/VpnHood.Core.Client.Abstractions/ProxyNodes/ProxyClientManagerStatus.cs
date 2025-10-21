namespace VpnHood.Core.Client.Abstractions.ProxyNodes;

public class ProxyClientManagerStatus
{
    public ProxyNodeInfo[] ProxyNodeInfos { get; init; } = [];
    public bool IsLastConnectionSuccessful { get; init; }
    public bool AutoUpdate { get; set; }
}