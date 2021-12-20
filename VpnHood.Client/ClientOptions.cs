using System;
using System.Net;
using VpnHood.Common.Net;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Client
{
    public class ClientOptions
    {
        /// <summary>
        ///     a never used IPv4 that must be outside the local network
        /// </summary>
        public IPAddress TcpProxyLoopbackAddressIpV4 { get; set; } = IPAddress.Parse("11.0.0.0");
        /// <summary>
        ///     a never used IPv6 ip that must be outside the machine
        /// </summary>
        public IPAddress TcpProxyLoopbackAddressIpV6 { get; set; } = IPAddress.Parse("2000::");

        public IPAddress[] DnsServers { get; set; } = { IPAddress.Parse("8.8.8.8"), IPAddress.Parse("8.8.4.4"), 
            IPAddress.Parse("2001:4860:4860::8888"), IPAddress.Parse("2001:4860:4860::8844") };
        public bool AutoDisposePacketCapture { get; set; } = true;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
        public Version Version { get; set; } = typeof(ClientOptions).Assembly.GetName().Version;
        public bool UseUdpChannel { get; set; } = false;
        public bool ExcludeLocalNetwork { get; set; } = true;
        public IpRange[]? IncludeIpRanges { get; set; }
        public IpRange[]? PacketCaptureIncludeIpRanges { get; set; }
        public SocketFactory SocketFactory { get; set; } = new();
        public int MaxDatagramChannelCount { get; set; } = 4;
        public string UserAgent { get; set; } = Environment.OSVersion.ToString();

#if  DEBUG
        public int ProtocolVersion { get; set; }
#endif
    }
}