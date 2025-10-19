namespace VpnHood.Core.Client.Abstractions.ProxyNodes;

public class ProxyManagerStatus
{
    public ProxyNodeInfo[] ProxyNodeInfos { get; init; } = [];
    public bool IsLastConnectionSuccessful { get; init; }
}