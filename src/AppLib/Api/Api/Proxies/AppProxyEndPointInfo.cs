namespace VpnHood.AppLib.Api.Proxies;

// One saved proxy: the proxy, how it has been behaving, and where it is. Flat on purpose - this
// used to derive from the engine's ProxyEndPointInfo, which published whatever that type grew.
public class AppProxyEndPointInfo
{
    public required ProxyEndPoint EndPoint { get; set; }
    public ProxyEndPointStatus Status { get; set; } = new();
    public required string? CountryCode { get; set; }
}
