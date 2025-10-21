namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions;

public class ProxyEndPointInfo
{
    public required ProxyEndPoint EndPoint { get; set; }
    public ProxyEndPointStatus Status { get; set; } = new();
}