using System;

namespace VpnHood.AccessServer.Caching;

public class AccessCache
{
    public long CycleSentTraffic { get; set; }
    public long CycleReceivedTraffic { get; set; }
    public DateTime ModifiedTime { get; } = DateTime.UtcNow;
}