using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace VpnHood.Common.Net;

public static class IpNetworkExtension
{
    public static IOrderedEnumerable<IpNetwork> Sort(this IEnumerable<IpNetwork> ipNetworks)
    {
        return IpNetwork.Sort(ipNetworks);
    }

    public static IEnumerable<IpRange> ToIpRanges(this IEnumerable<IpNetwork> ipNetworks)
    {
        return IpNetwork.ToIpRange(ipNetworks);
    }

    public static IEnumerable<IpNetwork> ToIpNetworks(this IEnumerable<IpRange> ipRanges)
    {
        return IpNetwork.FromIpRange(ipRanges);
    }

    public static IEnumerable<IpNetwork> ToIpNetwork(this IpRange ipRange)
    {
        return IpNetwork.FromIpRange(ipRange.FirstIpAddress, ipRange.LastIpAddress);
    }

    public static IEnumerable<IpNetwork> Invert(this IEnumerable<IpNetwork> ipNetworks, bool includeIPv4 = true, bool includeIPv6 = true)
    {
        return IpNetwork.Invert(ipNetworks, includeIPv4, includeIPv6);
    }

    public static IEnumerable<IpNetwork> Intersect(this IEnumerable<IpNetwork> ipNetworks1, IEnumerable<IpNetwork> ipNetworks2)
    {
        return IpNetwork.Intersect(ipNetworks1, ipNetworks2);
    }

    public static IOrderedEnumerable<IpRange> Sort(this IEnumerable<IpRange> ipRanges, bool unify = true)
    {
        return IpRange.Sort(ipRanges, unify);
    }

    public static IEnumerable<IpRange> Invert(this IEnumerable<IpRange> ipRanges,
        bool includeIPv4 = true, bool includeIPv6 = true)
    {
        return IpRange.Invert(ipRanges, includeIPv4, includeIPv6);
    }

    public static bool IsInRangeFast(this IpRange[] sortedIpRanges, IPAddress ipAddress)
    {
        return IpRange.IsInRangeFast(sortedIpRanges, ipAddress);
    }

    public static IpRange? FindRangeFast(this IpRange[] sortedIpRanges, IPAddress ipAddress)
    {
        return IpRange.FindRangeFast(sortedIpRanges, ipAddress);
    }

    public static IEnumerable<IpRange> Intersect(this IEnumerable<IpRange> ipRanges1, IEnumerable<IpRange> ipRanges2)
    {
        return IpRange.Intersect(ipRanges1, ipRanges2);
    }
}