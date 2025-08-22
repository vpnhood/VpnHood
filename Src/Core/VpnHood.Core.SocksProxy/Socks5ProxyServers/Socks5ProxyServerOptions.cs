using System.Net;

namespace VpnHood.Core.SocksProxy.Socks5ProxyServers;

public class Socks5ProxyServerOptions
{
    public required IPEndPoint ListenEndPoint { get; init; }

    public string? Username { get; init; }
    public string? Password { get; init; }

    public int Backlog { get; init; } = 512;
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(15);
}
