using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Filtering.Abstractions;

public class StaticIpFilter : IIpFilter
{
    private readonly IIpFilter? _nextFilter;
    private readonly bool _autoDisposeNextFilter;

    public event EventHandler? Changed;

    public StaticIpFilter(IIpFilter? nextFilter, bool autoDisposeNextFilter = true)
    {
        _nextFilter = nextFilter;
        _autoDisposeNextFilter = autoDisposeNextFilter;

        // roll a change announced below this stage up the pipe (this stage's own sets are unaffected)
        if (nextFilter != null)
            nextFilter.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    // the setters raise Changed: the ranges are set/replaced at runtime (e.g. the server∩device allow
    // set arrives with the session), and the caches above must not serve verdicts of the old sets
    public IpRangeOrderedList BlockedRanges {
        get;
        set { field = value; Changed?.Invoke(this, EventArgs.Empty); }
    } = [];

    public IpRangeOrderedList ExcludeRanges {
        get;
        set { field = value; Changed?.Invoke(this, EventArgs.Empty); }
    } = [];

    public IpRangeOrderedList IncludeRanges {
        get;
        set { field = value; Changed?.Invoke(this, EventArgs.Empty); }
    } = [];

    public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint)
    {
        var result = _nextFilter?.Process(protocol, endPoint) ?? FilterAction.Default;
        if (result != FilterAction.Default)
            return result;

        // blocked IP ranges
        if (BlockedRanges.Count > 0 && BlockedRanges.Contains(endPoint.Address))
            return FilterAction.Block;

        // excluded IP ranges
        if (ExcludeRanges.Count > 0 && ExcludeRanges.Contains(endPoint.Address))
            return FilterAction.Exclude;

        // included IP ranges: a veto gate like every stage — a non-empty include set excludes non-members
        // and members pass as Default ("no objection"). Include is never returned by IP gates; it is an
        // explicit override lane (domain force-list, ICMP force).
        if (IncludeRanges.Count > 0 && !IncludeRanges.Contains(endPoint.Address))
            return FilterAction.Exclude;

        return FilterAction.Default;
    }

    // this stage's own sets are program state, not external configuration; just forward the command
    public void Reconfigure() => _nextFilter?.Reconfigure();

    public bool IsEmpty =>
        BlockedRanges.Count == 0 &&
        ExcludeRanges.Count == 0 &&
        IncludeRanges.Count == 0 &&
        (_nextFilter?.IsEmpty ?? true);

    public void Dispose()
    {
        if (_autoDisposeNextFilter)
            _nextFilter?.Dispose();
    }
}