using VpnHood.Core.Client.Abstractions.ProxyNodes;

namespace VpnHood.AppLib.Services.Proxies;

public class AppProxyNodeInfo : ProxyNodeInfo
{
    public required string? CountryCode { get; set; }
}