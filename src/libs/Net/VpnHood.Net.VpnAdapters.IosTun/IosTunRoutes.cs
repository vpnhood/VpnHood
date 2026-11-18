using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.VpnAdapters.IosTun;

// The IPv6 routes a tunnel is given on iOS. Plain .NET, so the tests compile it too, off the device.
internal static class IosTunRoutes
{
    // On a network with no IPv6, iOS holds back AAAA lookups unless the tunnel has the default route ::/0, and was
    // seen to send no IPv6 into the tunnel without 2000::/3 beside it (measured on a device, 2026-06-10). The two
    // take all IPv6 into the tunnel, so they join the routes only where the run's own routes take every global
    // address already: there they bring in nothing more that the internet can reach. Where the routes take a part
    // of the global space, as a split's one network, or all but the networks it leaves out, the two would bring in
    // the split's other side, so the routes stay as they are. iOS may then hold back AAAA lookups on such a network,
    // and names are reached by IPv4, which the IPv4 routes split. By what the routes cover, not by their lengths:
    // one /48 is as much a split as one /64.
    public static IReadOnlyList<IpNetwork> GetIpV6Routes(IReadOnlyList<IpNetwork> routes, bool isIpV6OnNetwork)
    {
        if (isIpV6OnNetwork || !TakesAllGlobalAddresses(routes))
            return routes;

        return new[] { IpNetwork.AllV6, IpNetwork.AllGlobalUnicastV6 }
            .Where(x => !routes.Contains(x))
            .Concat(routes)
            .ToArray();
    }

    private static bool TakesAllGlobalAddresses(IReadOnlyList<IpNetwork> routes)
    {
        return new[] { IpNetwork.AllGlobalUnicastV6 }.ToIpRanges().Exclude(routes.ToIpRanges()).IsNone();
    }
}
