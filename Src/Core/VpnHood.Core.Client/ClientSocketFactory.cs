using System.Net.Sockets;
using VpnHood.Core.Adapters.Abstractions;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Core.Client;

internal class ClientSocketFactory(
    IVpnAdapter vpnAdapter,
    ISocketFactory socketFactory)
    : ISocketFactory
{
    public TcpClient CreateTcpClient(AddressFamily addressFamily)
    {
        var tcpClient = socketFactory.CreateTcpClient(addressFamily);

        // config for client
        socketFactory.SetKeepAlive(tcpClient.Client, true);
        VhUtils.ConfigTcpClient(tcpClient, null, null);

        // auto protect
        if (vpnAdapter.CanProtectSocket)
            vpnAdapter.ProtectSocket(tcpClient.Client);

        return tcpClient;
    }

    public UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var ret = socketFactory.CreateUdpClient(addressFamily);
        if (vpnAdapter.CanProtectSocket)
            vpnAdapter.ProtectSocket(ret.Client);
        return ret;
    }

    public void SetKeepAlive(Socket socket, bool enable)
    {
        socketFactory.SetKeepAlive(socket, enable);
    }
}