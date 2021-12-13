using System;
using System.Threading;

namespace VpnHood.Tunneling
{
    public class TunnelOptions
    {
        public TimeSpan TcpTimeout { get; set; }
        public int MaxDatagramChannelCount { get; set; }
    }
}