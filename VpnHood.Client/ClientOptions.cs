using Microsoft.Extensions.Logging;
using System;
using System.Net;

namespace VpnHood.Client
{
    public class ClientOptions
    {
        public int TcpIpChannelCount { get; set; } = 4;
        public IpResolveMode IpResolveMode { get; set; } = IpResolveMode.DnsThenToken;
        /// <summary>
        /// a never used ip that must be outside the machine
        /// </summary>
        public IPAddress TcpProxyLoopbackAddress { get; set; } = IPAddress.Parse("10.255.255.255");
        public IPAddress DnsAddress { get; set; } =  IPAddress.Parse("8.8.8.8");
        public bool LeaveDeviceOpen { get; set; } = false;
    }
}