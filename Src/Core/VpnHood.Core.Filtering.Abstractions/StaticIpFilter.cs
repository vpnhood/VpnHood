using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Filtering.Abstractions;

public class StaticIpFilter(IIpFilter? ipFilter) : IIpFilter
{
    public IpRangeOrderedList BlockedIpRanges { get; set; } = [];
    public IpRangeOrderedList ExcludeRanges { get; set; } = [];
    public IpRangeOrderedList IncludeRanges { get; set; } = [];

    public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint)
    {
        // blocked IP ranges
        if (BlockedIpRanges.Count > 0 && BlockedIpRanges.IsInRange(endPoint.Address))
            return FilterAction.Block;

        // excluded IP ranges
        if (ExcludeRanges.Count > 0 && ExcludeRanges.IsInRange(endPoint.Address))
            return FilterAction.Exclude;

        // included IP ranges
        if (IncludeRanges.Count > 0 && IncludeRanges.IsInRange(endPoint.Address))
            return FilterAction.Include;

        return ipFilter?.Process(protocol, endPoint) ?? FilterAction.Default;
    }

    public void Dispose()
    {
    }
}