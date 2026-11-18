using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.VpnAdapters.IosTun;

namespace VpnHood.Test.Tests;

// The IPv6 routes the iOS adapter gives its tunnel, compiled into this project from the iOS library's source (see the
// project file). What iOS does with them shows on a device alone.
[TestClass]
public class IosTunRoutesTest
{
    private static IpNetwork[] Parse(params string[] networks)
    {
        return networks.Select(IpNetwork.Parse).ToArray();
    }

    [TestMethod]
    public void Routes_that_take_every_global_address_get_the_two_global_routes_on_a_network_with_no_IPv6()
    {
        // a full tunnel that leaves the local networks out: the global addresses are what is left
        var result = IosTunRoutes.GetIpV6Routes(Parse("2000::/3"), isIpV6OnNetwork: false);
        CollectionAssert.AreEqual(Parse("::/0", "2000::/3"), result.ToArray());

        // the same in pieces, with a DNS server's own route beside them
        result = IosTunRoutes.GetIpV6Routes(Parse("2000::/4", "3000::/4", "fd00::53/128"), isIpV6OnNetwork: false);
        CollectionAssert.AreEqual(Parse("::/0", "2000::/3", "2000::/4", "3000::/4", "fd00::53/128"), result.ToArray());

        // a full tunnel that takes the local networks too has the default route already
        result = IosTunRoutes.GetIpV6Routes(Parse("::/0"), isIpV6OnNetwork: false);
        CollectionAssert.AreEqual(Parse("2000::/3", "::/0"), result.ToArray());
    }

    [TestMethod]
    [DataRow("2001:db8:1::/48")] // one network of a split, wider than /64: the two routes took all IPv6 with it
    [DataRow("2000::/4")]
    [DataRow("2001:db8:1:2::/64")]
    [DataRow("fd00::53/128")]
    public void A_splits_routes_stay_as_they_are_on_a_network_with_no_IPv6(string network)
    {
        var routes = Parse(network);

        var result = IosTunRoutes.GetIpV6Routes(routes, isIpV6OnNetwork: false);

        CollectionAssert.AreEqual(routes, result.ToArray());
    }

    [TestMethod]
    public void Routes_that_leave_a_network_out_stay_as_they_are_on_a_network_with_no_IPv6()
    {
        // every global address but one network the split sends around the tunnel
        var routes = new[] { IpNetwork.AllGlobalUnicastV6 }.ToIpRanges()
            .Exclude(IpNetwork.Parse("2001:db8:1::/48").ToIpRange())
            .ToIpNetworks()
            .ToArray();

        var result = IosTunRoutes.GetIpV6Routes(routes, isIpV6OnNetwork: false);

        CollectionAssert.AreEqual(routes, result.ToArray());
    }

    [TestMethod]
    public void A_network_with_IPv6_gets_the_routes_as_they_are()
    {
        var routes = Parse("2000::/3");

        var result = IosTunRoutes.GetIpV6Routes(routes, isIpV6OnNetwork: true);

        CollectionAssert.AreEqual(routes, result.ToArray());
    }

    [TestMethod]
    public void No_routes_stay_none()
    {
        var result = IosTunRoutes.GetIpV6Routes([], isIpV6OnNetwork: false);

        Assert.IsEmpty(result);
    }
}
