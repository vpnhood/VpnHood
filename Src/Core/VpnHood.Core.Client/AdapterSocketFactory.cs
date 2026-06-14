using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client;

public class AdapterSocketFactory(ISocketFactory socketFactory, IVpnAdapter vpnAdapter) : ISocketFactory
{
    public TcpClient CreateTcpClient(IPEndPoint ipEndPoint)
    {
        var tcpClient = socketFactory.CreateTcpClient(ipEndPoint);
        vpnAdapter.ProtectSocket(tcpClient.Client, ipEndPoint.Address);
        return tcpClient;
    }

    public UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var udpClient = socketFactory.CreateUdpClient(addressFamily);
        vpnAdapter.ProtectSocket(udpClient.Client);
        return udpClient;
    }
}
