using System.Net.Sockets;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Test.Factory
{
    public class TestSocketFactory : SocketFactory
    {
        private readonly bool _autoProtect;

        public TestSocketFactory(bool autoProtect)
        {
            _autoProtect = autoProtect;
        }

        public override TcpClient CreateTcpClient(AddressFamily addressFamily)
        {
            return TestNetProtector.CreateTcpClient(addressFamily, _autoProtect);
        }


        public override UdpClient CreateUdpClient(AddressFamily addressFamily)
        {
            return TestNetProtector.CreateUdpClient(addressFamily, _autoProtect);
        }
    }
}