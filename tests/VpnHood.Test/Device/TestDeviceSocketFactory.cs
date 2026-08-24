using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Quic.MsQuic;
using VpnHood.Core.Toolkit.Sockets;

namespace VpnHood.Test.Device;

public class TestDeviceSocketFactory(TestDevice testDevice) : MsQuicSocketFactory
{
    public override TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null)
    {
        var tcpClient = base.CreateTcpClient(ipEndPoint, options);
        ProtectSocket(tcpClient.Client);
        return tcpClient;
    }

    public override UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var udpClient = base.CreateUdpClient(addressFamily);
        ProtectSocket(udpClient.Client);
        return udpClient;
    }

    // The base returns a raw unprotected socket; route through CreateUdpClient so it gets protected.
    public override Socket CreateUdpSocket(AddressFamily addressFamily) => CreateUdpClient(addressFamily).Client;

    private void ProtectSocket(Socket socket)
    {
        if (testDevice.VpnService?.CurrentVpnAdapter?.CanProtectSocket == true)
            testDevice.VpnService?.CurrentVpnAdapter.ProtectSocket(socket);
    }
}
