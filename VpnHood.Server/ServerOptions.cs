using VpnHood.Server.Factory;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace VpnHood.Server
{
    public class ServerOptions
    {
        public IPEndPoint TcpHostEndPoint { get; set; } = new IPEndPoint(IPAddress.Any, 443);
        public X509Certificate2 Certificate { get; set; }
        public TcpClientFactory TcpClientFactory { get; set; } = new TcpClientFactory();
        public UdpClientFactory UdpClientFactory { get; set; } = new UdpClientFactory();
    }
}


