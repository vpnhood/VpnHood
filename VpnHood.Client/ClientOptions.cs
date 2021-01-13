using System.Net;

namespace VpnHood.Client
{
    public class ClientOptions
    {
        public int ReconnectDelay { get; set; } = 5000;
        public int MaxReconnectCount { get; set; } = 3;
        public int MinDatagramChannelCount { get; set; } = 2;
        /// <summary>
        /// a never used ip that must be outside the machine
        /// </summary>
        public IPAddress TcpProxyLoopbackAddress { get; set; } = IPAddress.Parse("10.255.255.255");
        public IPAddress DnsAddress { get; set; } =  IPAddress.Parse("8.8.8.8");
        public bool LeavePacketCaptureOpen { get; set; } = false;
        public int Timeout { get; set; } = 60 * 1000;
    }
}