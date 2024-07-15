using System.Net.Sockets;
using VpnHood.Client.Device.WinDivert;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Test.Services;

public class TestSocketFactory : SocketFactory
{
    public override TcpClient CreateTcpClient(AddressFamily addressFamily)
    {
        var tcpClient = base.CreateTcpClient(addressFamily);
        tcpClient.Client.Ttl = WinDivertPacketCapture.ProtectedTtl;
        return tcpClient;
    }

    public override UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var udpClient = base.CreateUdpClient(addressFamily);
        udpClient.Client.Ttl = WinDivertPacketCapture.ProtectedTtl;
        return udpClient;
    }
}