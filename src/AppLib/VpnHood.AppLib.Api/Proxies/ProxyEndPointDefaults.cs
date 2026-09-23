namespace VpnHood.AppLib.Api.Proxies;

// What to assume for the parts a typed-in proxy address leaves out.
public class ProxyEndPointDefaults
{
    public bool? IsEnabled { get; set; }
    public ProxyProtocol? Protocol { get; set; }
    public int? Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}
