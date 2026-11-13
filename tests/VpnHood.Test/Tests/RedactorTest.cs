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
    public void A_packets_text_keeps_all_but_its_public_addresses()
    {
        var text = Redactor.RedactPacketText("Src=8.8.8.8, Dst=10.0.0.1, Proto=Udp, Len=44");
        StringAssert.StartsWith(text, "Src=v4-");
        StringAssert.EndsWith(text, ", Dst=10.0.0.1, Proto=Udp, Len=44");
        Assert.IsFalse(text.Contains("8.8.8.8"));

        // a value at the text's end, and one that is no address
        StringAssert.StartsWith(Redactor.RedactPacketText("Proto=Udp, Dst=8.8.4.4"), "Proto=Udp, Dst=v4-");
        Assert.AreEqual("Src=***, Len=1", Redactor.RedactPacketText("Src=8.8.8.8:53, Len=1"));

        // a text with none of the labels, and a longer label that only ends as one of them
        Assert.AreEqual("Proto=Udp, Len=44", Redactor.RedactPacketText("Proto=Udp, Len=44"));
        Assert.AreEqual("NatSrc=8.8.8.8", Redactor.RedactPacketText("NatSrc=8.8.8.8"));
    }

    [TestMethod]
    [DataRow("a.b", "***")]
    [DataRow("abcd", "***")]
    [DataRow("ab.cd", "***b.cd")]
    [DataRow("abcd.com", "***.com")]
    [DataRow("example.com", "ex***.com")]
    public void A_host_name_is_hidden_by_its_length(string hostName, string expected)
    {
        Assert.AreEqual(expected, Redactor.RedactHostName(hostName));
    }

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
