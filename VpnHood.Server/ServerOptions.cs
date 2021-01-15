using VpnHood.Server.Factory;
using System.Net;
using VpnHood.Common.Trackers;

namespace VpnHood.Server
{
    public class ServerOptions
    {
        public IPEndPoint TcpHostEndPoint { get; set; } = new IPEndPoint(IPAddress.Any, 443);
        public TcpClientFactory TcpClientFactory { get; set; } = new TcpClientFactory();
        public UdpClientFactory UdpClientFactory { get; set; } = new UdpClientFactory();
        public ITracker Tracker { get; set; }

        /// <summary>
        /// A unique identifier for each instance of server. can be null
        /// </summary>
        public string ServerId { get; set; } 
        public bool IsDiagnoseMode { get; set; } 
    }
}


