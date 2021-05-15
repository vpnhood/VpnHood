using System.Net;

namespace VpnHood.Client
{
    public class ClientOptions
    {
        public int MinDatagramChannelCount { get; set; } = 4;
        /// <summary>
        /// a never used ip that must be outside the machine
        /// </summary>
        public IPAddress TcpProxyLoopbackAddress { get; set; } = IPAddress.Parse("11.0.0.0");
        public IPAddress DnsAddress { get; set; } =  IPAddress.Parse("8.8.8.8");
        public bool LeavePacketCaptureOpen { get; set; } = false;
        public int Timeout { get; set; } = 60 * 1000;
        public string Version { get; set; } = typeof(ClientOptions).Assembly.GetName().Version.ToString(3);
    }
}