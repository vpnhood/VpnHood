using System.Net;

namespace VpnHood.Core.SocksProxy.Socks5Proxy;

public readonly record struct Socks5CommandResult(
    Socks5Command Command,
    Socks5CommandReply Reply,
    IPAddress? BoundAddress,
    int BoundPort)
{
    public IPEndPoint? BoundEndPoint => BoundAddress == null ? null : new IPEndPoint(BoundAddress, BoundPort);
}