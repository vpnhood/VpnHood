using System.Net;
using System.Net.Sockets;

namespace VpnHood.Core.SocksProxy.Socks5Proxy;

public static class TcpClientSocks5Extensions
{
    public static Task ConnectViaSocks5ProxyAsync(this TcpClient tcpClient, IPEndPoint remoteEp,
        Socks5Options options, CancellationToken cancellationToken)
    {
        var socks5Proxy = new Socks5Client(options);
        return socks5Proxy.ConnectAsync(tcpClient, remoteEp, cancellationToken);
    }
}