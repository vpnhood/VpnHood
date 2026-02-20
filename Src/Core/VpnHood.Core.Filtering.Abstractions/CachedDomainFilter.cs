using VpnHood.Core.Toolkit.Collections;

namespace VpnHood.Core.Filtering.Abstractions;

public class CachedDomainFilter(IDomainFilter netFilter, TimeSpan timeout) : IDomainFilter
{
    private readonly TimeoutDictionary<string, TimeoutItem<FilterAction>> _cache = new(timeout);
    public FilterAction Process(string? domain)
    {
        if (domain == null)
            return netFilter.Process(domain);

        if (_cache.TryGetValue(domain, out var cachedAction))
            return cachedAction.Value;

        var action = netFilter.Process(domain);
        _cache.TryAdd(domain, new TimeoutItem<FilterAction>(action));
        return action;
    }

    public void ClearCache()
    {
        _cache.Cleanup();
    }

    public void Dispose()
    {
        _cache.Dispose();
    }
}