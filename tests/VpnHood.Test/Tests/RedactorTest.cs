using System.Net;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Test.Tests;

// NAT64 addresses carry the IPv4 address they reach, so a log redacts them as it redacts that address.
[TestClass]
public class RedactorTest : TestBase
{
    private static readonly Redactor Redactor = new(isAnonymousMode: true);

    [TestMethod]
    public void A_well_known_nat64_address_is_the_ipv4_it_carries()
    {
        var token = Redactor.RedactIpAddress(IPAddress.Parse("64:ff9b::8.8.4.4"));
        StringAssert.StartsWith(token, "v4-");
        Assert.AreEqual(Redactor.RedactIpAddress(IPAddress.Parse("8.8.4.4")), token);
    }

    [TestMethod]
    public void A_local_use_nat64_address_is_redacted_whole()
    {
        StringAssert.StartsWith(Redactor.RedactIpAddress(IPAddress.Parse("64:ff9b:1:808:4:400::")), "v6-");
    }

    [TestMethod]
    public void A_nat64_prefix_that_carries_no_public_address_stays_readable()
    {
        Assert.AreEqual("64:ff9b::/96", Redactor.RedactIpNetwork(IpNetwork.Parse("64:ff9b::/96")));
        Assert.AreEqual("64:ff9b::a00:1", Redactor.RedactIpAddress(IPAddress.Parse("64:ff9b::10.0.0.1")));
    }
}
