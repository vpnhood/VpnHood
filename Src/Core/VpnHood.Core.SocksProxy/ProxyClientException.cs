using System.Net.Sockets;

namespace VpnHood.Core.SocksProxy;

public class ProxyClientException(SocketError socketError, string? message = null)
    : SocketException((int)socketError, message);
