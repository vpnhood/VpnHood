using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Toolkit.Sockets;

namespace VpnHood.Test.Device;

public class TestDeviceSocketFactory(TestDevice testDevice) : ISocketFactory
{
    public TcpClient CreateTcpClient(IPEndPoint ipEndPoint)
    {
        var tcpClient = new TcpClient(ipEndPoint.AddressFamily);
        if (testDevice.VpnService?.CurrentVpnAdapter?.CanProtectSocket == true)
            testDevice.VpnService?.CurrentVpnAdapter.ProtectSocket(tcpClient.Client);

        return tcpClient;
    }

    public UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var udpClient = new UdpClient(addressFamily);
        if (testDevice.VpnService?.CurrentVpnAdapter?.CanProtectSocket == true)
            testDevice.VpnService?.CurrentVpnAdapter.ProtectSocket(udpClient.Client);

        return udpClient;
    }
}