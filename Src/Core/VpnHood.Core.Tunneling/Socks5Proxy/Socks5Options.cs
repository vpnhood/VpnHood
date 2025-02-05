using System.Net;

namespace VpnHood.Core.Tunneling.Socks5Proxy;

public class Socks5Options
{
    public required IPEndPoint ProxyEndPoint { get; init; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}
