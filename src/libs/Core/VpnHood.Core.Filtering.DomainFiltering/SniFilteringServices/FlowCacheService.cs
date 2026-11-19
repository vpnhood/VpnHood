using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace VpnHood.Core.Filtering.DomainFiltering.SniFilteringServices;

internal class FlowCacheService(TimeSpan flowTimeout) : IDisposable
{
    private readonly ConcurrentDictionary<FlowKey, FlowInfo> _flowCache = new();
    private readonly long _flowTimeoutTicks = flowTimeout.Ticks;
    private readonly long _sweepIntervalTicks = TimeSpan.FromSeconds(Math.Min(flowTimeout.TotalSeconds, 60)).Ticks;
    private long _nextSweepTicks;
    private bool _disposed;

    public bool TryGetValue(FlowKey key, [NotNullWhen(true)] out FlowInfo? value)
    {
        return _flowCache.TryGetValue(key, out value);
    }

    // ReSharper disable once OutParameterValueIsAlwaysDiscarded.Global
    public bool TryRemove(FlowKey key, out FlowInfo? value)
    {
        return _flowCache.TryRemove(key, out value);
    }

    public void Set(FlowKey key, FlowInfo value)
    {
        _flowCache[key] = value;
    }

    /// <summary>
    /// Removes the flows unused for the timeout, once an interval; any other call costs a comparison.
    /// </summary>
    /// <remarks>
    /// Called with each packet, on the packets' own thread: a flow is used and refreshed there alone, so the sweep
    /// removes none that a packet has refreshed since it looked, which a sweep on a thread of its own did. A removed
    /// flow's held packets go to the GC undisposed.
    /// </remarks>
    public void RemoveExpired(long nowTicks)
    {
        if (nowTicks < _nextSweepTicks)
            return;

        _nextSweepTicks = nowTicks + _sweepIntervalTicks;
        foreach (var kvp in _flowCache) {
            // the flow the look found, not one that took its key since: after an adapter restart an old reader may
            // run beside the new one for a moment
            if (nowTicks - kvp.Value.LastSeenTicks > _flowTimeoutTicks)
                _flowCache.TryRemove(kvp);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        // the flows' packets are left to the GC, as the sweep's are
        _flowCache.Clear();
        GC.SuppressFinalize(this);
    }
}
