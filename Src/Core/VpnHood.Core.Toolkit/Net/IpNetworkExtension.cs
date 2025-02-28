namespace VpnHood.Core.Toolkit.Net;

public static class IpNetworkExtension
{
    public static IOrderedEnumerable<IpNetwork> Sort(this IEnumerable<IpNetwork> ipNetworks)
    {
        return ipNetworks
            .ToIpRanges()
            .ToIpNetworks();
    }

    public static IpRangeOrderedList ToIpRanges(this IEnumerable<IpNetwork> ipNetworks)
    {
        return ipNetworks
            .Select(x => x.ToIpRange())
            .ToOrderedList();
    }

    public static bool IsAll(this IOrderedEnumerable<IpNetwork> ipNetworks)
    {
        return ipNetworks.SequenceEqual(IpNetwork.All);
    }

    public static bool IsAllV4(this IOrderedEnumerable<IpNetwork> ipNetworks)
    {
        return ipNetworks.SequenceEqual([IpNetwork.AllV4]);
    }

    public static bool IsAllV6(this IOrderedEnumerable<IpNetwork> ipNetworks)
    {
        return ipNetworks.SequenceEqual([IpNetwork.AllV6]);
    }

}