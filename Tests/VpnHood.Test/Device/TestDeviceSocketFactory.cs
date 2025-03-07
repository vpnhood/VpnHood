using System.Net.Sockets;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Device;

public class TestDeviceSocketFactory(TestDevice testDevice) : ISocketFactory
{
    private readonly TestSocketFactory _socketFactory = new();
    public TcpClient CreateTcpClient(AddressFamily addressFamily)
    {
        return testDevice.VpnService?.CurrentVpnAdapter!=null 
            ? testDevice.VpnService.CurrentVpnAdapter.CreateProtectedTcpClient(addressFamily) 
            : _socketFactory.CreateTcpClient(addressFamily);
    }

    public UdpClient CreateUdpClient(AddressFamily addressFamily)
    {
        return testDevice.VpnService?.CurrentVpnAdapter != null
            ? testDevice.VpnService.CurrentVpnAdapter.CreateProtectedUdpClient(addressFamily)
            : _socketFactory.CreateUdpClient(addressFamily);
    }

    public void SetKeepAlive(Socket socket, bool enable)
    {
        _socketFactory.SetKeepAlive(socket, enable);
    }
}