using VpnHood.Core.Toolkit.Collections;

namespace VpnHood.Core.Filtering.Abstractions;

public class CachedDomainFilter : IDomainFilter
{
    private readonly IDomainFilter? _nextFilter;
    private readonly bool _autoDisposeNextFilter;
    private readonly TimeoutDictionary<string, TimeoutItem<FilterAction>> _cache;

    public event EventHandler? Changed;

    public CachedDomainFilter(IDomainFilter? nextFilter, TimeSpan timeout, bool autoDisposeNextFilter = true)
    {
        _nextFilter = nextFilter;
        _autoDisposeNextFilter = autoDisposeNextFilter;
        _cache = new TimeoutDictionary<string, TimeoutItem<FilterAction>>(timeout);

        // self-invalidation: when the inner chain announces a change (a live gate swap somewhere below),
        // drop every memoized verdict so none outlives the chain that produced it, and roll the event up
        if (nextFilter != null)
            nextFilter.Changed += (_, _) => {
                ClearCache();
                Changed?.Invoke(this, EventArgs.Empty);
            };
    }

    public FilterAction Process(string? domain)
    {
        if (domain == null)
            return _nextFilter?.Process(domain) ?? FilterAction.Default;

        if (_cache.TryGetValue(domain, out var cachedAction))
            return cachedAction.Value;

        var action = _nextFilter?.Process(domain) ?? FilterAction.Default;
        _cache.TryAdd(domain, new TimeoutItem<FilterAction>(action));
        return action;
    }

    // nothing external to re-read here; invalidation comes back up via Changed if a stage below swaps
    public void Reconfigure() => _nextFilter?.Reconfigure();

    // a cache holds no rules of its own
    public bool IsEmpty => _nextFilter?.IsEmpty ?? true;

    // Drop EVERY cached verdict (Cleanup would only evict expired ones).
    public void ClearCache()
    {
        _cache.RemoveAll();
    }

    public void Dispose()
    {
        _cache.Dispose();
        if (_autoDisposeNextFilter)
            _nextFilter?.Dispose();
    }
}
