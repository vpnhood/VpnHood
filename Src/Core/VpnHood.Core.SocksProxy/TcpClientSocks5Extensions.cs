using System.Net;
using System.Net.Sockets;
using VpnHood.Core.SocksProxy.Socks5ProxyClients;

namespace VpnHood.Core.SocksProxy;

public static class TcpClientSocks5Extensions
{
    public static Task ConnectViaSocks5ProxyAsync(this TcpClient tcpClient, IPEndPoint remoteEp,
        Socks5ClientProxyOptions clientProxyOptions, CancellationToken cancellationToken)
    {
        var socks5Proxy = new Socks5ProxyClient(clientProxyOptions);
        return socks5Proxy.ConnectAsync(tcpClient, remoteEp, cancellationToken);
    }
}