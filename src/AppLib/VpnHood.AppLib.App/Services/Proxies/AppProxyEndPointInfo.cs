using VpnHood.Core.Proxies.EndPointManagement.Abstractions;

namespace VpnHood.AppLib.Services.Proxies;

public class AppProxyEndPointInfo : ProxyEndPointInfo
{
    public required string? CountryCode { get; set; }
}