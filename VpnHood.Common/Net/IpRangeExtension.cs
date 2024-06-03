namespace VpnHood.Common.Net;

public static class IpRangeExtension
{
    public static IpRangeOrderedList ToOrderedList(this IEnumerable<IpRange> ipRanges)
    {
        return new IpRangeOrderedList(ipRanges);
    }

    public static IOrderedEnumerable<IpNetwork> ToIpNetworks(this IEnumerable<IpRange> ipRanges)
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
        throw new NotSupportedException($"Use {nameof(IpRangeOrderedList)}.Intersect.");
    }
}