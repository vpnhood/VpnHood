using VpnHood.Server.Factory;
using System.Net;
using VpnHood.Common;

namespace VpnHood.Server
{
    public class ServerOptions
    {
        public IPEndPoint TcpHostEndPoint { get; set; } = new IPEndPoint(IPAddress.Any, 443);
        public TcpClientFactory TcpClientFactory { get; set; } = new TcpClientFactory();
        public UdpClientFactory UdpClientFactory { get; set; } = new UdpClientFactory();
        public ITracker Tracker { get; set; }
    }
}


