using System.Net.Sockets;
using VpnHood.Core.Client.Device.WinDivert;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Test.Providers;

public class TestSocketFactory : SocketFactory
{
    public override TcpClient CreateTcpClient(AddressFamily addressFamily)
    {
        var tcpClient = base.CreateTcpClient(addressFamily);
        tcpClient.Client.Ttl = WinDivertVpnAdapter.ProtectedTtl;
        return tcpClient;
    }

    public override UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var udpClient = base.CreateUdpClient(addressFamily);
        udpClient.Client.Ttl = WinDivertVpnAdapter.ProtectedTtl;
        return udpClient;
    }
}