using VpnHood.Core.Toolkit.Collections;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Filtering.Abstractions;

public class CachedIpFilter : IIpFilter
{
    private readonly IIpFilter _nextFilter;
    private readonly bool _autoDisposeNextFilter;
    private readonly TimeoutDictionary<IpEndPointValue, TimeoutItem<FilterAction>> _cache;

    public event EventHandler? Changed;

    public CachedIpFilter(IIpFilter nextFilter, TimeSpan timeout, bool autoDisposeNextFilter = true)
    {
        _nextFilter = nextFilter;
        _autoDisposeNextFilter = autoDisposeNextFilter;
        _cache = new TimeoutDictionary<IpEndPointValue, TimeoutItem<FilterAction>>(timeout);

        // self-invalidation: when the inner chain announces a change (a live gate swap somewhere below),
        // drop every memoized verdict so none outlives the chain that produced it, and roll the event up
        nextFilter.Changed += (_, _) => {
            ClearCache();
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint)
    {
        if (_cache.TryGetValue(endPoint, out var cachedAction))
            return cachedAction.Value;

        var action = _nextFilter.Process(protocol, endPoint);
        _cache.TryAdd(endPoint, new TimeoutItem<FilterAction>(action));
        return action;
    }

    // nothing external to re-read here; invalidation comes back up via Changed if a stage below swaps
    public void Reconfigure() => _nextFilter.Reconfigure();

    // a cache holds no rules of its own
    public bool IsEmpty => _nextFilter.IsEmpty;

    // Drop EVERY cached verdict (Cleanup would only evict expired ones).
    public void ClearCache()
    {
        _cache.RemoveAll();
    }

    public void Dispose()
    {
        _cache.Dispose();
        if (_autoDisposeNextFilter)
            _nextFilter.Dispose();
    }
}
