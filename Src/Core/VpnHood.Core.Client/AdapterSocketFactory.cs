using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client;

// Creates sockets via the given factory and protects them (when supported) so they bypass the VPN
// adapter. ProtectSocket binds the socket itself and rejects an already-bound one, so on the protect
// path the socket is created unbound here rather than via the (binding) inner factory.
// Keep-alive / no-delay / kernel buffer config is applied by the outer ConfiguringSocketFactory.
public class AdapterSocketFactory(IVpnAdapter vpnAdapter, ISocketFactory socketFactory) : ISocketFactory
{
    public TcpClient CreateTcpClient(IPEndPoint ipEndPoint)
    {
        if (!vpnAdapter.CanProtectSocket)
            return socketFactory.CreateTcpClient(ipEndPoint);

        var tcpClient = new TcpClient(ipEndPoint.AddressFamily);
        vpnAdapter.ProtectSocket(tcpClient.Client, ipEndPoint.Address);
        return tcpClient;
    }

    public UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        if (!vpnAdapter.CanProtectSocket)
            return socketFactory.CreateUdpClient(addressFamily);

        // zero is needed to make sure OS assigns a free port and bind it
        var udpClient = new UdpClient(addressFamily);
        if (udpClient.Client is null)
            throw new Exception("UdpClient socket is null.");

        vpnAdapter.ProtectSocket(udpClient.Client);
        return udpClient;
    }
}
