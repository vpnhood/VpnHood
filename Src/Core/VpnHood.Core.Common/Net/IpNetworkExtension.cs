namespace VpnHood.Core.Common.Net;

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
}