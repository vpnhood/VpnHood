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
        var tcpClient = vpnAdapter.CanProtectClient
            ? vpnAdapter.CreateProtectedTcpClient(addressFamily)
            : socketFactory.CreateTcpClient(addressFamily);

        // config for client
        socketFactory.SetKeepAlive(tcpClient.Client, true);
        VhUtils.ConfigTcpClient(tcpClient, null, null);
        return tcpClient;
    }

    public UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var ret = vpnAdapter.CanProtectClient
            ? vpnAdapter.CreateProtectedUdpClient(addressFamily)
            : socketFactory.CreateUdpClient(addressFamily);

        return ret;
    }

    public void SetKeepAlive(Socket socket, bool enable)
    {
        socketFactory.SetKeepAlive(socket, enable);
    }
}