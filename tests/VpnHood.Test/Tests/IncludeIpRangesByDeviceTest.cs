using System.Net;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Test.Tests;

// The routes the device takes into the tunnel (ClientHelper.BuildIncludeIpRangesByDevice) never start inside a
// loopback: Android refuses such a route ("Bad address") and the connect fails. A loopback a route covers whole
// stays, so a list without such an edge keeps its routes.
[TestClass]
public class IncludeIpRangesByDeviceTest : TestBase
{
    private static IpRangeOrderedList Build(IpRangeOrderedList clientIncludeIpRanges, IPAddress hostIpAddress,
        bool includeLocalNetwork = false)
    {
        return ClientHelper.BuildIncludeIpRangesByDevice(
            clientIncludeIpRanges: clientIncludeIpRanges,
            serverIncludeIpRanges: IpNetwork.All.ToIpRanges(),
            unroutedIpMode: SplitUnsupportedIpMode.Exclude,
            unsupportedIpV6Mode: SplitUnsupportedIpMode.Block,
            isIpV6SupportedByServer: true,
            includeLocalNetwork: includeLocalNetwork,
            hostIpAddress: hostIpAddress,
            dnsConfig: new DnsConfig {
                DnsSelection = DnsSelection.UserDns,
                IsIncludedInVpn = false,
                IsUserSuppressed = false,
                DnsServers = [IPAddress.Parse("8.8.8.8")]
            });
    }

    private static void AssertNoLoopbackRoute(IpRangeOrderedList includeIpRanges)
    {
        foreach (var network in includeIpRanges.ToIpNetworks())
            Assert.IsFalse(IPAddress.IsLoopback(network.Prefix), $"A route starts inside the loopback: {network}");
    }

    [TestMethod]
    public void A_server_at_a_loopback_address_leaves_no_route_there()
    {
        var includeIpRanges = Build(IpNetwork.All.ToIpRanges(), IPAddress.Loopback);

        AssertNoLoopbackRoute(includeIpRanges);
        Assert.IsTrue(includeIpRanges.Contains(IPAddress.Parse("8.8.4.4")), "the rest is still routed");
    }

    [TestMethod]
    public void A_split_whose_edge_falls_at_the_loopback_leaves_no_route_there()
    {
        // a via-device list that excludes 126.0.0.0/8: what it leaves starts at 127.0.0.0
        var includeIpRanges = Build(
            IpNetwork.All.ToIpRanges().Exclude(IpNetwork.Parse("126.0.0.0/8").ToIpRange()),
            IPAddress.Parse("51.81.81.250"));

        AssertNoLoopbackRoute(includeIpRanges);
        Assert.IsTrue(includeIpRanges.Contains(IPAddress.Parse("128.0.0.1")), "the range after the loopback is routed");
        Assert.IsFalse(includeIpRanges.Contains(IPAddress.Parse("126.0.0.1")), "the split still holds");
    }

    [TestMethod]
    public void An_IPv6_edge_at_the_loopback_leaves_no_route_there()
    {
        // what is left after ::/128 starts at ::1; the local networks stay in, as they hold ::1
        var includeIpRanges = Build(
            IpNetwork.All.ToIpRanges().Exclude(IpNetwork.Parse("::/128").ToIpRange()),
            IPAddress.Parse("51.81.81.250"), includeLocalNetwork: true);

        AssertNoLoopbackRoute(includeIpRanges);
        Assert.IsTrue(includeIpRanges.Contains(IPAddress.Parse("127.0.0.1")),
            "the IPv4 loopback, inside a larger route, is not touched");
    }

    [TestMethod]
    public void A_loopback_a_route_covers_whole_stays()
    {
        // no edge inside either loopback: the routes are what they were, the loopbacks inside larger ones
        var hostIpAddress = IPAddress.Parse("51.81.81.250");
        var expected = IpNetwork.All.ToIpRanges()
            .Exclude(IpNetwork.LocalNetworks.ToIpRanges())
            .Exclude(IpNetwork.MulticastNetworks.ToIpRanges())
            .Exclude(IPAddress.Broadcast)
            .Exclude(hostIpAddress);

        var includeIpRanges = Build(IpNetwork.All.ToIpRanges(), hostIpAddress);

        CollectionAssert.AreEqual(expected.ToIpNetworks().Select(x => x.ToString()).ToArray(),
            includeIpRanges.ToIpNetworks().Select(x => x.ToString()).ToArray());
        Assert.IsTrue(includeIpRanges.Contains(IPAddress.Parse("127.0.0.1")), "the IPv4 loopback is inside a larger route");
    }
}
