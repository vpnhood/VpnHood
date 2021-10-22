using System;
using VpnHood.Common.Trackers;
using VpnHood.Server.SystemInformation;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Server
{
    public class ServerOptions
    {
        public SocketFactory SocketFactory { get; set; } = new();
        public ITracker? Tracker { get; set; }
        public int OrgStreamReadBufferSize { get; set; } = TunnelUtil.StreamBufferSize;
        public int TunnelStreamReadBufferSize { get; set; } = TunnelUtil.StreamBufferSize;
        public int MaxDatagramChannelCount { get; set; } = TunnelUtil.MaxDatagramChannelCount;
        public ISystemInfoProvider? SystemInfoProvider { get; set; }
        public TimeSpan ConfigureInterval { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan UpdateStatusInterval { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan CheckMaintenanceInterval { get; set; } = TimeSpan.FromMinutes(1);
        public bool AutoDisposeAccessServer { get; set; } = true;
        public long AccessSyncCacheSize { get; set; } = 100 * 1000000;
    }
}