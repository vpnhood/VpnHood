using System.Net;

namespace VpnHood.Core.SocksProxy.Socks5ProxyClients;

public readonly record struct Socks5Endpoint(string? Host, IPAddress? Address, int Port);
