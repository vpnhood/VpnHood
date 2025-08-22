using System.Net;

namespace VpnHood.Core.SocksProxy.Socks5ProxyClients;

public class Socks5ClientProxyOptions
{
    public required IPEndPoint ProxyEndPoint { get; init; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}