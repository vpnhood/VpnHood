using VpnHood.Core.Client.Abstractions.ProxyNodes;

namespace VpnHood.AppLib.Services.Proxies;

public class AppProxyNodeInfo(ProxyNode proxyNode) : ProxyNodeInfo(proxyNode)
{
    public required string? CountryCode { get; set; }
}