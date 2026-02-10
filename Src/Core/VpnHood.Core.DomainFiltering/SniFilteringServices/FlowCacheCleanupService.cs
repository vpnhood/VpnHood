using System.Collections.Concurrent;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.DomainFiltering.SniFilteringServices;

internal class FlowCacheCleanupService : IDisposable
{
    private readonly ConcurrentDictionary<IpEndPointValue, FlowInfo> _flowCache;
    private readonly TimeSpan _flowTimeout;
    private readonly Job _cleanupJob;
    private bool _disposed;

    public FlowCacheCleanupService(
        ConcurrentDictionary<IpEndPointValue, FlowInfo> flowCache,
        TimeSpan flowTimeout)
    {
        _flowCache = flowCache;
        _flowTimeout = flowTimeout;

        var cleanupInterval = TimeSpan.FromSeconds(Math.Min(flowTimeout.TotalSeconds, 60));
        _cleanupJob = new Job(CleanupExpiredFlows, cleanupInterval, "FlowCacheCleanup");
    }

    private ValueTask CleanupExpiredFlows(CancellationToken cancellationToken)
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        var nowTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
        var flowTimeoutTicks = _flowTimeout.Ticks;

        foreach (var kvp in _flowCache) {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (nowTicks - kvp.Value.LastSeenTicks <= flowTimeoutTicks)
                continue;

            if (!_flowCache.TryRemove(kvp.Key, out var flowInfo))
                continue;

            // Dispose buffered packets
            foreach (var packet in flowInfo.BufferedPackets)
                packet.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cleanupJob.Dispose();
    }
}
