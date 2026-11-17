using VpnHood.Core.Client;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Test.Tests;

// The app's split indicator (SessionInfo.IsTrafficSplitByServer) lights only for public space the server leaves out
[TestClass]
public class TrafficSplitByServerTest : TestBase
{
    private static bool IsSplit(IEnumerable<IpNetwork> excludeNetworks, bool isIpV6SupportedByServer = true)
    {
        return ClientHelper.IsTrafficSplitByServer(
            serverIncludeIpRangesByApp: IpNetwork.All.ToIpRanges(),
            serverIncludeIpRangesByDevice: IpNetwork.All.ToIpRanges().Exclude(excludeNetworks.ToIpRanges()),
            isIpV6SupportedByServer: isIpV6SupportedByServer);
    }

    [TestMethod]
    public void A_server_that_leaves_out_every_IPv6_address_outside_2000_3_does_not_split()
    {
        // what a server left out as local before LocalNetworksV6 held fc00::/7 and fe80::/10 alone
        Assert.IsFalse(IsSplit(IpNetwork.LocalNetworksV4.Concat(IpNetwork.AllGlobalUnicastV6.Invert())));
    }

    [TestMethod]
    [DataRow("100.64.0.0/10")]
    [DataRow("192.168.0.0/16")]
    [DataRow("2001:db8::/32")]
    [DataRow("fc00::/7")]
    public void A_network_that_is_not_public_does_not_split(string network)
    {
        Assert.IsFalse(IsSplit([IpNetwork.Parse(network)]));
    }

    [TestMethod]
    [DataRow("8.8.8.0/24", true, true)]
    [DataRow("2001:4860::/32", true, true)]
    [DataRow("2001:4860::/32", false, false)] // a family the server carries not at all is IsIpV6SupportedByServer's
    public void Public_space_left_out_splits(string network, bool isIpV6SupportedByServer, bool expected)
    {
        Assert.AreEqual(expected, IsSplit([IpNetwork.Parse(network)], isIpV6SupportedByServer));
    }
}
