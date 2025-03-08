using System.Net.Sockets;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client;

public class AdapterSocketFactory(
    IVpnAdapter vpnAdapter,
    ISocketFactory socketFactory)
    : ISocketFactory
{
    public TcpClient CreateTcpClient(AddressFamily addressFamily)
    {
        var tcpClient = socketFactory.CreateTcpClient(addressFamily);
        if (vpnAdapter.CanProtectSocket)
            vpnAdapter.ProtectSocket(tcpClient.Client);

        // config for client
        socketFactory.SetKeepAlive(tcpClient.Client, true);
        VhUtils.ConfigTcpClient(tcpClient, null, null);
        return tcpClient;
    }

    public UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var udpClient = socketFactory.CreateUdpClient(addressFamily);
        if (vpnAdapter.CanProtectSocket)
            vpnAdapter.ProtectSocket(udpClient.Client);

        return udpClient;
    }

    public void SetKeepAlive(Socket socket, bool enable)
    {
        socketFactory.SetKeepAlive(socket, enable);
    }
}