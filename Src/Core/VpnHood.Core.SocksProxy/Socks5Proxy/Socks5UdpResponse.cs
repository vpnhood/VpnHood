using System.Net;

namespace VpnHood.Core.SocksProxy.Socks5Proxy;

public readonly record struct Socks5UdpResponse(string? Host, IPAddress? Address, int Port);