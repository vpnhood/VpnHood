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

    public static IOrderedEnumerable<IpNetwork> ToIpNetworks(this IEnumerable<IpRange> ipRanges)
    {
        return IpNetwork.FromIpRange(ipRanges);
    }

    public static IEnumerable<IpNetwork> ToIpNetworks(this IpRange ipRange)
    {
        return IpNetwork.FromIpRange(ipRange.FirstIpAddress, ipRange.LastIpAddress);
    }

    public static IOrderedEnumerable<IpNetwork> Invert(this IEnumerable<IpNetwork> ipNetworks, bool includeIPv4 = true, bool includeIPv6 = true)
    {
        return IpNetwork.Invert(ipNetworks, includeIPv4, includeIPv6);
    }

    public static IOrderedEnumerable<IpNetwork> Intersect(this IEnumerable<IpNetwork> ipNetworks1, IEnumerable<IpNetwork> ipNetworks2)
    {
        return IpNetwork.Intersect(ipNetworks1, ipNetworks2);
    }

    public static IOrderedEnumerable<IpNetwork> Exclude(this IEnumerable<IpNetwork> ipNetworks, IEnumerable<IpNetwork> excludeIpNetworks)
    {
        return IpNetwork.Exclude(ipNetworks, excludeIpNetworks);
    }

    public static bool IsAll(this IOrderedEnumerable<IpNetwork> ipNetworks)
    {
        return IpNetwork.IsAll(ipNetworks);
    }


    public static IOrderedEnumerable<IpRange> Exclude(this IEnumerable<IpRange> ipRanges, IEnumerable<IpRange> excludeIpRanges)
    {
        return IpRange.Exclude(ipRanges, excludeIpRanges);
    }

    public static IOrderedEnumerable<IpRange> Sort(this IEnumerable<IpRange> ipRanges, bool unify = true)
    {
        return IpRange.Sort(ipRanges, unify);
    }

    public static IOrderedEnumerable<IpRange> Invert(this IEnumerable<IpRange> ipRanges,
        bool includeIPv4 = true, bool includeIPv6 = true)
    {
        return IpRange.Invert(ipRanges, includeIPv4, includeIPv6);
    }

    public static bool IsInSortedRanges(this IpRange[] sortedIpRanges, IPAddress ipAddress)
    {
        return IpRange.IsInSortedRanges(sortedIpRanges, ipAddress);
    }

    public static IpRange? FindInSortedRanges(this IpRange[] sortedIpRanges, IPAddress ipAddress)
    {
        return IpRange.FindInSortedRanges(sortedIpRanges, ipAddress);
    }

    public static IOrderedEnumerable<IpRange> Intersect(this IEnumerable<IpRange> ipRanges1, IEnumerable<IpRange> ipRanges2)
    {
        return IpRange.Intersect(ipRanges1, ipRanges2);
    }
}