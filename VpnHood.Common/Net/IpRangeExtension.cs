using System.Linq;

namespace VpnHood.Common.Net;

public static class IpRangeExtension
{
    public static IpRangeOrderedList ToOrderedList(this IEnumerable<IpRange> ipRanges)
    {
        return new IpRangeOrderedList(ipRanges);
    }

    public static bool IsAll(this IpRangeOrderedList ipRanges)
    {
        return ipRanges.ToIpNetworks().IsAll();
    }

    public static bool IsEmpty(this IpRangeOrderedList ipRanges)
    {
        return !ipRanges.ToIpNetworks().Any();
    }

    public static IOrderedEnumerable<IpNetwork> ToIpNetworks(this IpRangeOrderedList ipRanges)
    {
        var ipNetworkList = new List<IpNetwork>();
        foreach (var ipRange in ipRanges)
            ipNetworkList.AddRange(ipRange.ToIpNetworks());

        return ipNetworkList.OrderBy(x => x.FirstIpAddress, new IPAddressComparer());
    }

    public static IEnumerable<IpNetwork> ToIpNetworks(this IpRange ipRange)
    {
        return IpNetwork.FromRange(ipRange.FirstIpAddress, ipRange.LastIpAddress);
    }

    public static IEnumerable<IpRange> Intersect(this IEnumerable<IpRange> first, IEnumerable<IpRange> second)
    {
        // prevent use Linq.Intersect in mistake. it is bug prone.
        throw new NotSupportedException($"Use {nameof(IpRangeOrderedList)}.Intersect.");
    }
}