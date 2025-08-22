using System.Net;

namespace VpnHood.Core.SocksProxy.Socks4ProxyClients;

public class Socks4ProxyClientOptions
{
    public required IPEndPoint ProxyEndPoint { get; init; }

    // Optional user id (null-terminated string in the request). Can be empty.
    public string? UserName { get; init; }
}
