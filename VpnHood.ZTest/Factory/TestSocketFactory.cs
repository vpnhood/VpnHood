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

        public override TcpClient CreateTcpClient()
        {
            return TestNetProtector.CreateTcpClient(_autoProtect);
        }


        public override UdpClient CreateUdpClient()
        {
            return TestNetProtector.CreateUdpClient(_autoProtect);
        }
    }
}