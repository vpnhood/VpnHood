using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client;

internal class AdapterSocketFactory(ISocketFactory socketFactory, IVpnAdapter vpnAdapter) : ISocketFactory
{
    public TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
    {
        var tcpClient = socketFactory.CreateTcpClient(ipEndPoint, options);
        vpnAdapter.ProtectSocket(tcpClient.Client, ipEndPoint.Address);
        return tcpClient;
    }

    public UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var udpClient = socketFactory.CreateUdpClient(addressFamily);
        vpnAdapter.ProtectSocket(udpClient.Client);
        return udpClient;
    }

    public Socket CreateUdpSocket(AddressFamily addressFamily)
    {
        var socket = socketFactory.CreateUdpSocket(addressFamily);
        vpnAdapter.ProtectSocket(socket);
        return socket;
    }
}
