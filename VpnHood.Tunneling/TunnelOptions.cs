using System;

namespace VpnHood.Tunneling
{
    public class TunnelOptions
    {
        public int MaxDatagramChannelCount { get; set; } = 8;
        public TimeSpan TcpTimeout { get; set; } = TunnelUtil.TcpTimeout;
    }
}