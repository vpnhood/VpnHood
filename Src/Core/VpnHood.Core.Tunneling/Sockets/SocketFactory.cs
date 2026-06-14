using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Sockets;

namespace VpnHood.Core.Tunneling.Sockets;

// Plain socket creator. Post-creation config (keep-alive, no-delay, kernel buffer sizes) is applied
// by ConfiguringSocketFactory on the client, or via VhUtils.ConfigTcpClient on the server.
public class SocketFactory : ISocketFactory
{
    public virtual TcpClient CreateTcpClient(IPEndPoint ipEndPoint)
    {
        var localEndPoint = new IPEndPoint(ipEndPoint.IsV4() ? IPAddress.Any : IPAddress.IPv6Any, 0);
        var tcpClient = new TcpClient(ipEndPoint.AddressFamily);
        tcpClient.Client.Bind(localEndPoint);
        return tcpClient;
    }

    public virtual UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var localEndPoint = new IPEndPoint(addressFamily.IsV4() ? IPAddress.Any : IPAddress.IPv6Any, 0);
        var udpClient = new UdpClient(addressFamily);
        udpClient.Client.Bind(localEndPoint);
        return udpClient;
    }
}
