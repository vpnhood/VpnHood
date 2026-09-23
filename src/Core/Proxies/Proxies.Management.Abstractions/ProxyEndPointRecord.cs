namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>A proxy endpoint row in the endpoint store: identity + status + app-managed extras.</summary>
public class ProxyEndPointRecord
{
    public required ProxyEndPoint EndPoint { get; set; }
    public ProxyEndPointStatus Status { get; set; } = new();
    public string? CountryCode { get; set; }

    public ProxyEndPointInfo ToInfo() => new() { EndPoint = EndPoint, Status = Status };

    public static ProxyEndPointRecord FromInfo(ProxyEndPointInfo info, string? countryCode = null) =>
        new() { EndPoint = info.EndPoint, Status = info.Status, CountryCode = countryCode };
}
