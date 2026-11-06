using System.Net;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.VpnAdapters.LinuxTun;

namespace VpnHood.Test.Tests;

[TestClass]
public class LinuxTunAdapterAliasTest
{
    [TestMethod]
    [DataRow("nameserver 127.0.0.53\noptions edns0 trust-ad\nsearch .\n", true)]
    [DataRow("# a comment\r\nnameserver 127.0.0.54\r\n", true)]
    [DataRow("nameserver 8.8.8.8\nnameserver 8.8.4.4\n", true)]
    [DataRow("nameserver 192.168.1.1\n", false)]
    [DataRow("nameserver 127.0.0.53\nnameserver 192.168.1.1\n", false)]
    [DataRow("nameserver 127.0.0.1\n", false)]
    [DataRow("# nameserver 127.0.0.53\n", false)]
    [DataRow("", false)]
    public void ResolvConfMustUseTheVpnDns(string resolvConf, bool expected)
    {
        var vpnDns = new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Parse("8.8.4.4") };
        Assert.AreEqual(expected, LinuxTunVpnAdapter.IsResolvConfOnVpnDns(resolvConf, vpnDns));
    }

    [TestMethod]
    public void DnsRedirectCoversTheLocalNetworkAndItsServers()
    {
        var ruleset = LinuxTunVpnAdapter.CreateDnsRedirectRuleset("VpnHoodClient", ["eth0"],
            [IpNetwork.Parse("192.168.1.20/24")],
            ["192.168.1.1", "10.0.0.2", "8.8.8.8", "127.0.0.53", "fe80::1%2", "not-an-address"],
            [IPAddress.Parse("8.8.8.8"), IPAddress.Parse("8.8.4.4")]);

        // the way in from the local network and this machine's own address stay as they are
        StringAssert.Contains(ruleset, "iifname { \"eth0\" } return\n");
        StringAssert.Contains(ruleset, "ip daddr { 192.168.1.20 } return\n");

        // a server on the network is inside it, one off it is added, and the rest are not asked about
        StringAssert.Contains(ruleset, "ip daddr { 10.0.0.2/32, 192.168.1.0/24 } udp dport 53 dnat to 8.8.8.8\n");
        StringAssert.Contains(ruleset, "ip daddr { 10.0.0.2/32, 192.168.1.0/24 } tcp dport 53 dnat to 8.8.8.8\n");
    }

    [TestMethod]
    public void DnsRedirectRefusesAFamilyTheVpnDnsDoesNotHave()
    {
        var localNetworks = new[] { IpNetwork.Parse("192.168.1.20/24"), IpNetwork.Parse("fd00:1::20/64") };

        var v4Only = LinuxTunVpnAdapter.CreateDnsRedirectRuleset("vh", ["eth0"], localNetworks, [],
            [IPAddress.Parse("8.8.8.8")]);
        StringAssert.Contains(v4Only, "ip daddr { 192.168.1.0/24 } udp dport 53 dnat to 8.8.8.8\n");
        StringAssert.Contains(v4Only, "type filter hook forward priority 0;");
        StringAssert.Contains(v4Only, "ip6 daddr { fd00:1::/64 } udp dport 53 reject\n");
        StringAssert.Contains(v4Only, "ip6 daddr { fd00:1::/64 } tcp dport 53 reject with tcp reset\n");

        var v6Only = LinuxTunVpnAdapter.CreateDnsRedirectRuleset("vh", ["eth0"], localNetworks, [],
            [IPAddress.Parse("2001:4860:4860::8888")]);
        StringAssert.Contains(v6Only, "ip daddr { 192.168.1.0/24 } udp dport 53 reject\n");
        StringAssert.Contains(v6Only, "ip6 daddr { fd00:1::20 } return\n");
        StringAssert.Contains(v6Only, "ip6 daddr { fd00:1::/64 } udp dport 53 dnat to 2001:4860:4860::8888\n");
    }

    [TestMethod]
    public void DnsRedirectAlwaysClearsTheOldTables()
    {
        var ruleset = LinuxTunVpnAdapter.CreateDnsRedirectRuleset("vh", [], [], [], [IPAddress.Parse("8.8.8.8")]);
        Assert.AreEqual(
            "table ip vpnhood_dns_vh\ndelete table ip vpnhood_dns_vh\n" +
            "table ip6 vpnhood_dns_vh\ndelete table ip6 vpnhood_dns_vh\n", ruleset);
    }

    [TestMethod]
    public void ExistingAliasesRemainCompatible()
    {
        foreach (var appId in new[] { "com.vpnhood.client", "app:id/path_1-2", new string('a', 255) })
            Assert.AreEqual(appId, LinuxTunVpnAdapter.GetAdapterAlias(appId));
    }

    [TestMethod]
    [DataRow(256)]
    [DataRow(10000)]
    public void LongIdsHaveStableBoundedAliases(int length)
    {
        var appId = new string('a', length);
        var alias = LinuxTunVpnAdapter.GetAdapterAlias(appId);

        Assert.AreEqual(64 + 1 + 16, alias.Length);
        StringAssert.StartsWith(alias, new string('a', 64) + "-");
        Assert.AreEqual(alias, LinuxTunVpnAdapter.GetAdapterAlias(appId));
        Assert.AreNotEqual(alias, LinuxTunVpnAdapter.GetAdapterAlias(appId + "b"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("app name\twith\nwhitespace")]
    [DataRow("应用程序/شبكة/🚀")]
    [DataRow("app'; $(echo unexpected) `echo unexpected` \" & | > < \\ \0")]
    public void ArbitraryIdsProduceNonemptyCommandSafeAliases(string appId)
    {
        var alias = LinuxTunVpnAdapter.GetAdapterAlias(appId);

        Assert.IsTrue(alias.Length is > 0 and <= 255);
        Assert.IsTrue(alias.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or ':' or '/'));
        Assert.AreEqual(alias, LinuxTunVpnAdapter.GetAdapterAlias(appId));
    }

    [TestMethod]
    public void SanitizingAnIdDoesNotDiscardItsIdentity()
    {
        Assert.AreNotEqual(LinuxTunVpnAdapter.GetAdapterAlias("app one"),
            LinuxTunVpnAdapter.GetAdapterAlias("app?one"));
    }

    [TestMethod]
    public void DerivedAliasesIdentifyOnlyUnheldLeftoversOfTheSameApp()
    {
        var appId = new string('a', 1000);
        var alias = LinuxTunVpnAdapter.GetAdapterAlias(appId);
        var own = new LinuxTunInfo("old-tun", alias, IsHeld: false, IsDown: false);
        var foreign = own with { Alias = LinuxTunVpnAdapter.GetAdapterAlias(appId + "other") };

        Assert.IsTrue(LinuxTunVpnAdapter.IsOwnLeftover(own, "new-tun", alias));
        Assert.IsTrue(LinuxTunVpnAdapter.IsOwnLeftover(own with { IsDown = true }, "new-tun", alias));
        Assert.IsFalse(LinuxTunVpnAdapter.IsOwnLeftover(own with { IsHeld = true }, "old-tun", alias));
        Assert.IsFalse(LinuxTunVpnAdapter.IsOwnLeftover(foreign, "old-tun", alias));
        Assert.IsFalse(LinuxTunVpnAdapter.IsOwnLeftover(foreign with { IsDown = true }, "old-tun", alias));
        Assert.IsFalse(LinuxTunVpnAdapter.IsOwnLeftover(own, "old-tun", null));
    }
}
