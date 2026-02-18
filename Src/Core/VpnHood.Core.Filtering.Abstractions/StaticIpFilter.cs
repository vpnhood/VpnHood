using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Filtering.Abstractions;

public class StaticIpFilter(IIpFilter? nextFilter) : IIpFilter
{
    public IpRangeOrderedList BlockedRanges { get; set; } = [];
    public IpRangeOrderedList ExcludeRanges { get; set; } = [];
    public IpRangeOrderedList IncludeRanges { get; set; } = [];

    public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint)
    {
        var result = nextFilter?.Process(protocol, endPoint) ?? FilterAction.Default;
        if (result != FilterAction.Default)
            return result;

        // blocked IP ranges
        if (BlockedRanges.Count > 0 && BlockedRanges.Contains(endPoint.Address))
            return FilterAction.Block;

        // excluded IP ranges
        if (ExcludeRanges.Count > 0 && ExcludeRanges.Contains(endPoint.Address))
            return FilterAction.Exclude;

        // included IP ranges
        if (IncludeRanges.Count > 0 && IncludeRanges.Contains(endPoint.Address))
            return FilterAction.Include;

        return FilterAction.Default;
    }

    public void Dispose()
    {
    }
}