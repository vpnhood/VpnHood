using System.Net.Sockets;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Test.Services;

public class TestSocketFactory : SocketFactory
{
    public override TcpClient CreateTcpClient(AddressFamily addressFamily)
    {
        var tcpClient = base.CreateTcpClient(addressFamily);
        TestSocketProtector.ProtectSocket(tcpClient.Client);
        return tcpClient;
    }

    public override UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var udpClient = base.CreateUdpClient(addressFamily);
        TestSocketProtector.ProtectSocket(udpClient.Client);
        return udpClient;
    }
}