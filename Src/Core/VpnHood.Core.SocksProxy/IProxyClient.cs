using System.Net;
using System.Net.Sockets;

namespace VpnHood.Core.SocksProxy;

public interface IProxyClient
{
    Task ConnectAsync(TcpClient tcpClient, string host, int port, CancellationToken cancellationToken);

    Task ConnectAsync(TcpClient tcpClient, IPEndPoint destination, CancellationToken cancellationToken);
}
