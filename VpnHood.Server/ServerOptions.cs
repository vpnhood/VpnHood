using System;
using System.Net;
using VpnHood.Common.Trackers;
using VpnHood.Server.SystemInformation;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Server
{
    public class ServerOptions
    {
        public IPEndPoint TcpHostEndPoint { get; set; } = new IPEndPoint(IPAddress.Any, 443);
        public SocketFactory SocketFactory { get; set; } = new();
        public ITracker Tracker { get; set; }

        /// <summary>
        /// A unique identifier for each instance of server. can be null
        /// </summary>
        public Guid? ServerId { get; set; }
        public int OrgStreamReadBufferSize { get; set; } = TunnelUtil.StreamBufferSize;
        public int TunnelStreamReadBufferSize { get; set; } = TunnelUtil.StreamBufferSize;
        public int MaxDatagramChannelCount { get; set; } = TunnelUtil.MaxDatagramChannelCount;
        public ISystemInfoProvider SystemInfoProvider { get; set; }
        public TimeSpan SubscribeInterval { get; set; } = TimeSpan.FromSeconds(60);
        public TimeSpan SendStatusInterval { get; set; } = TimeSpan.FromMinutes(5);
        public bool AutoDisposeAccessServer { get; set; } = true;
        public long AccessSyncCacheSize { get; set; } = 100 * 1000000;
    }
}


