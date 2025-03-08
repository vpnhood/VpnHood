using System.Net.Sockets;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Device;

public class TestDeviceSocketFactory(TestDevice testDevice) : ISocketFactory
{
    private readonly TestSocketFactory _socketFactory = new();
    public TcpClient CreateTcpClient(AddressFamily addressFamily)
    {
        var tcpClient = _socketFactory.CreateTcpClient(addressFamily);
        if (testDevice.VpnService?.CurrentVpnAdapter?.CanProtectSocket == true)
            testDevice.VpnService?.CurrentVpnAdapter.ProtectSocket(tcpClient.Client);

        return tcpClient;
    }

    public UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        var udpClient = _socketFactory.CreateUdpClient(addressFamily);
        if (testDevice.VpnService?.CurrentVpnAdapter?.CanProtectSocket == true)
            testDevice.VpnService?.CurrentVpnAdapter.ProtectSocket(udpClient.Client);

        return udpClient;
    }

    public void SetKeepAlive(Socket socket, bool enable)
    {
        _socketFactory.SetKeepAlive(socket, enable);
    }
}