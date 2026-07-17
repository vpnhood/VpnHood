namespace VpnHood.Core.Proxies.EndPointManagement.Abstractions;

public class ProxyEndPointDefaults
{
    public bool? IsEnabled { get; set; }
    public ProxyProtocol? Protocol { get; set; }
    public int? Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}