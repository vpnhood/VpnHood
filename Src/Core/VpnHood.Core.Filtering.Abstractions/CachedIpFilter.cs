using VpnHood.Core.Toolkit.Collections;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Filtering.Abstractions;

public class CachedIpFilter(IIpFilter? ipFilter, TimeSpan timeout) : IIpFilter
{
    private readonly TimeoutDictionary<IpEndPointValue, TimeoutItem<FilterAction>> _cache = new(timeout);

    public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint)
    {
        if (ipFilter == null)
            return FilterAction.Default;

        if (_cache.TryGetValue(endPoint, out var cachedAction))
            return cachedAction.Value;

        var action = ipFilter.Process(protocol, endPoint);
        _cache.TryAdd(endPoint, new TimeoutItem<FilterAction>(action));
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