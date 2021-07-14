using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Test.Factory
{
    public class TestSocketFactory : SocketFactory
    {
        private readonly bool _autoProtect;

        public TestSocketFactory(bool autoProtect) 
            => _autoProtect = autoProtect;

        public override TcpClient CreateTcpClient()
            => TestNetProtector.CreateTcpClient(_autoProtect);


        public override UdpClient CreateUdpClient()
            => TestNetProtector.CreateUdpClient(_autoProtect);
    }
}
