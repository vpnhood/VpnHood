using System.Net;

namespace VpnHood.Core.SocksProxy.Socks5Proxy;

public readonly record struct Socks5Endpoint(string? Host, IPAddress? Address, int Port)
{
    public bool IsDomain => Host != null;
    public bool HasIp => Address != null;
    public IPEndPoint? ToIPEndPoint() => Address == null ? null : new IPEndPoint(Address, Port);
}