using System.Net.Sockets;

namespace VpnHood.Core.SocksProxy.Socks5Proxy;

public class ProxyServerException(SocketError socketError, string? message = null)
    : SocketException((int)socketError, message);
