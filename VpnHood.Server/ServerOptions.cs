using System.Net;
using VpnHood.Common.Trackers;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Server
{
    public class ServerOptions
    {
        public IPEndPoint TcpHostEndPoint { get; set; } = new IPEndPoint(IPAddress.Any, 443);
        public SocketFactory SocketFactory { get; set; } = new ();
        public ITracker Tracker { get; set; }

        /// <summary>
        /// A unique identifier for each instance of server. can be null
        /// </summary>
        public string ServerId { get; set; } 
        public bool IsDiagnoseMode { get; set; }
        public int OrgStreamReadBufferSize { get; set; }
        public int TunnelStreamReadBufferSize { get; set; }
    }
}


