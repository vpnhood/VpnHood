using System.Net;

namespace VpnHood.Common.Net;

public static class IpNetworkExtension
{
    public static IOrderedEnumerable<IpNetwork> Sort(this IEnumerable<IpNetwork> ipNetworks)
    {
        var ipNetworkList = new List<IpNetwork>();
        foreach (var ipRange in ipNetworkList.ToIpRangesNew())
            ipNetworkList.AddRange(ipRange.ToIpNetworks());

        return ipNetworkList.OrderBy(x => x.FirstIpAddress, new IPAddressComparer());
    }

    public static IpRangeOrderedList ToIpRanges(this IEnumerable<IpNetwork> ipNetworks)
    {
        return ipNetworks.Select(x => x.ToIpRange()).ToOrderedList();
    }

    public static IpRangeOrderedList ToIpRangesNew(this IEnumerable<IpNetwork> ipNetworks)
    {
        return ipNetworks.Select(x => x.ToIpRange()).ToOrderedList();
    }


    public static IOrderedEnumerable<IpNetwork> ToIpNetworks(this IEnumerable<IpRange> ipRanges)
    {
        return IpNetwork.FromIpRange(ipRanges);
    }

    public static IEnumerable<IpNetwork> ToIpNetworks(this IpRange ipRange)
    {
        return IpNetwork.FromIpRange(ipRange.FirstIpAddress, ipRange.LastIpAddress);
    }

    public static bool IsAll(this IOrderedEnumerable<IpNetwork> ipNetworks)
    {
        return IpNetwork.IsAll(ipNetworks);
    }

    public static IOrderedEnumerable<IpRange> Exclude(this IEnumerable<IpRange> ipRanges, IEnumerable<IpRange> excludeIpRanges)
    {
        return IpRange.Exclude(ipRanges, excludeIpRanges);
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

    public static IOrderedEnumerable<IpRange> Union(this IEnumerable<IpRange> ipRanges1, IEnumerable<IpRange> ipRanges2)
    {
        return IpRange.Union(ipRanges1, ipRanges2);
    }
}