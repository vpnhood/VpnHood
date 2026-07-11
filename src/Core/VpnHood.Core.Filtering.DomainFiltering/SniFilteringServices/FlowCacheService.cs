using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Filtering.DomainFiltering.SniFilteringServices;

internal class FlowCacheService : IDisposable
{
    private readonly ConcurrentDictionary<IpEndPointValue, FlowInfo> _flowCache = new();
    private readonly TimeSpan _flowTimeout;
    private readonly Job _cleanupJob;
    private bool _disposed;

    public FlowCacheService(TimeSpan flowTimeout)
    {
        _flowTimeout = flowTimeout;

        var cleanupInterval = TimeSpan.FromSeconds(Math.Min(flowTimeout.TotalSeconds, 60));
        _cleanupJob = new Job(CleanupExpiredFlows, cleanupInterval, "FlowCacheCleanup");
    }

    public bool TryGetValue(IpEndPointValue key, [NotNullWhen(true)] out FlowInfo? value)
    {
        return _flowCache.TryGetValue(key, out value);
    }

    // ReSharper disable once OutParameterValueIsAlwaysDiscarded.Global
    public bool TryRemove(IpEndPointValue key, out FlowInfo? value)
    {
        return _flowCache.TryRemove(key, out value);
    }

    public void Set(IpEndPointValue key, FlowInfo value)
    {
        _flowCache[key] = value;
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

        // Dispose all buffered packets
        foreach (var kvp in _flowCache) {
            foreach (var packet in kvp.Value.BufferedPackets)
                packet.Dispose();
        }

        _flowCache.Clear();
        GC.SuppressFinalize(this);
    }
}
