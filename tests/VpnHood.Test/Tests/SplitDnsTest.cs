using System.Net;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Test.Tests;

// SplitDnsMode: whether DNS is kept inside the tunnel. Selection (GetDnsServers) partitions the user's
// resolvers into tunnelable (server routes them, filters pass) and outside-only (everything else — the
// runtime already routes those outside via the allow set or by never capturing them). DefaultRoute honors
// either kind; IncludeAll only accepts tunnelable ones and throws when nothing is. An in-tunnel plan is then
// made true by construction: BuildIncludeIpRangesByDevice widens the device set so the adapter captures its resolvers.
[TestClass]
public class SplitDnsTest : TestBase
{
    private static readonly IPAddress PublicDns = IPAddress.Parse("8.8.8.8");
    private static readonly IPAddress LanDns = IPAddress.Parse("192.168.1.1");
    private static readonly IPAddress HostIp = IPAddress.Parse("51.81.81.250");

    private static DnsConfig CreateDnsConfig(bool isIncludedInVpn, params IPAddress[] dnsServers)
    {
        return new DnsConfig {
            DnsSelection = DnsSelection.UserDns,
            IsIncludedInVpn = isIncludedInVpn,
            IsUserSuppressed = false,
            DnsServers = dnsServers
        };
    }

    [TestMethod]
    public void An_in_tunnel_plan_survives_the_device_exclusions()
    {
        // the user's via-device split does not cover the resolver; without the union the adapter would never
        // route it, and the in-tunnel plan could not be kept
        var includeIpRanges = ClientHelper.BuildIncludeIpRangesByDevice(
            includeIpRanges: new[] { IpRange.Parse("20.0.0.0-20.255.255.255") }.ToOrderedList(),
            canProtectSocket: true,
            includeLocalNetwork: false,
            hostIpAddress: HostIp,
            dnsConfig: CreateDnsConfig(isIncludedInVpn: true, PublicDns));

        Assert.IsTrue(includeIpRanges.Contains(PublicDns), "the plan's resolver must be routed by the adapter");
        Assert.IsTrue(includeIpRanges.Contains(IPAddress.Parse("20.0.0.1")), "the device list is preserved");
        Assert.IsFalse(includeIpRanges.Contains(IPAddress.Parse("9.9.9.9")),
            "only the resolver comes back, not everything around it");
    }

    [TestMethod]
    public void An_out_of_tunnel_plan_never_touches_the_device_set()
    {
        // an honored out-of-tunnel resolver (e.g. the Pi-hole) must stay outside: adding it would capture
        // its queries and tunnel them to a server that can not reach it
        var includeIpRanges = ClientHelper.BuildIncludeIpRangesByDevice(
            includeIpRanges: IpNetwork.All.ToIpRanges(),
            canProtectSocket: true,
            includeLocalNetwork: false,
            hostIpAddress: HostIp,
            dnsConfig: CreateDnsConfig(isIncludedInVpn: false, LanDns));

        Assert.IsFalse(includeIpRanges.Contains(LanDns));
    }

    [TestMethod]
    public void The_host_address_is_never_routed_into_the_tunnel()
    {
        // the tunnel is built over the host address; routing it inside itself would loop, so it wins over the
        // DNS union even when the user points DNS at the server itself
        var includeIpRanges = ClientHelper.BuildIncludeIpRangesByDevice(
            includeIpRanges: IpNetwork.All.ToIpRanges(),
            canProtectSocket: true,
            includeLocalNetwork: true,
            hostIpAddress: HostIp,
            dnsConfig: CreateDnsConfig(isIncludedInVpn: true, HostIp, PublicDns));

        Assert.IsFalse(includeIpRanges.Contains(HostIp));
        Assert.IsTrue(includeIpRanges.Contains(PublicDns));
    }

    [TestMethod]
    public void IncludeAll_does_not_honor_an_out_of_tunnel_user_resolver()
    {
        // the user's split excludes their own resolver — a deliberate out-of-tunnel choice, but IncludeAll
        // keeps DNS inside, so the exclusion is overridden and the resolver is used through the tunnel
        using var ipFilter = new StaticIpFilter(null) {
            ExcludeRanges = new[] { new IpRange(PublicDns) }.ToOrderedList()
        };

        var dnsConfig = ClientHelper.GetDnsServers(
            userDnsAddresses: [PublicDns],
            serverDnsAddresses: [IPAddress.Parse("9.9.9.9")],
            serverIncludeIpRanges: IpNetwork.All.ToIpRanges(),
            ipFilter: ipFilter,
            splitDnsMode: SplitDnsMode.IncludeAll);

        Assert.IsTrue(dnsConfig.IsIncludedInVpn);
        Assert.AreEqual(DnsSelection.UserDns, dnsConfig.DnsSelection);
        CollectionAssert.AreEquivalent(new[] { PublicDns }, dnsConfig.DnsServers);
    }

    [TestMethod]
    public void DefaultRoute_honors_an_out_of_tunnel_user_resolver()
    {
        using var ipFilter = new StaticIpFilter(null) {
            ExcludeRanges = new[] { new IpRange(PublicDns) }.ToOrderedList()
        };

        var dnsConfig = ClientHelper.GetDnsServers(
            userDnsAddresses: [PublicDns],
            serverDnsAddresses: [IPAddress.Parse("9.9.9.9")],
            serverIncludeIpRanges: IpNetwork.All.ToIpRanges(),
            ipFilter: ipFilter,
            splitDnsMode: SplitDnsMode.DefaultRoute);

        Assert.IsFalse(dnsConfig.IsIncludedInVpn, "the user's exclusion is a deliberate out-of-tunnel choice");
        Assert.IsFalse(dnsConfig.IsUserSuppressed, "honoring the user's own exclusion is not suppression");
        Assert.AreEqual(DnsSelection.UserDns, dnsConfig.DnsSelection);
    }

    [TestMethod]
    public void DefaultRoute_honors_a_lan_resolver_the_server_does_not_route()
    {
        // the Pi-hole case: a default server does not route local networks, so the resolver can only work
        // outside the tunnel — and the runtime already sends it there (never captured by the adapter).
        // Honoring it beats suppressing it: the user's ad-blocker keeps working.
        using var ipFilter = new StaticIpFilter(null); // no app-level exclusion

        var dnsConfig = ClientHelper.GetDnsServers(
            userDnsAddresses: [LanDns],
            serverDnsAddresses: [IPAddress.Parse("9.9.9.9")],
            serverIncludeIpRanges: IpNetwork.All.ToIpRanges().Exclude(IpNetwork.LocalNetworks.ToIpRanges()),
            ipFilter: ipFilter,
            splitDnsMode: SplitDnsMode.DefaultRoute);

        Assert.IsFalse(dnsConfig.IsIncludedInVpn);
        Assert.IsFalse(dnsConfig.IsUserSuppressed, "DefaultRoute never suppresses: outside is allowed");
        Assert.AreEqual(DnsSelection.UserDns, dnsConfig.DnsSelection);
        CollectionAssert.AreEquivalent(new[] { LanDns }, dnsConfig.DnsServers);
    }

    [TestMethod]
    public void IncludeAll_suppresses_a_lan_user_resolver_to_server_dns()
    {
        // same Pi-hole, opposite mode: outside is not allowed and the server does not route local networks,
        // so the server's resolver is used instead of a claim the session can not keep
        using var ipFilter = new StaticIpFilter(null);
        var serverDns = IPAddress.Parse("9.9.9.9");

        var dnsConfig = ClientHelper.GetDnsServers(
            userDnsAddresses: [LanDns],
            serverDnsAddresses: [serverDns],
            serverIncludeIpRanges: IpNetwork.All.ToIpRanges().Exclude(IpNetwork.LocalNetworks.ToIpRanges()),
            ipFilter: ipFilter,
            splitDnsMode: SplitDnsMode.IncludeAll);

        Assert.IsTrue(dnsConfig.IsUserSuppressed);
        Assert.AreEqual(DnsSelection.ServerDns, dnsConfig.DnsSelection);
        CollectionAssert.AreEquivalent(new[] { serverDns }, dnsConfig.DnsServers);
    }

    [TestMethod]
    public void A_server_routed_internal_resolver_stays_usable_under_IncludeAll()
    {
        // the flip side of the LAN rejection: a server that DELIBERATELY routes its internal ranges (e.g. a
        // corporate resolver behind the tunnel) advertised them, so its word makes the resolver tunnelable —
        // a client-side "private address" heuristic would have wrongly rejected it
        using var ipFilter = new StaticIpFilter(null);
        var internalDns = IPAddress.Parse("10.0.0.53");

        var dnsConfig = ClientHelper.GetDnsServers(
            userDnsAddresses: [internalDns],
            serverDnsAddresses: [IPAddress.Parse("9.9.9.9")],
            serverIncludeIpRanges: IpNetwork.All.ToIpRanges(), // this server routes its internal ranges too
            ipFilter: ipFilter,
            splitDnsMode: SplitDnsMode.IncludeAll);

        Assert.IsTrue(dnsConfig.IsIncludedInVpn);
        Assert.AreEqual(DnsSelection.UserDns, dnsConfig.DnsSelection);
        CollectionAssert.AreEquivalent(new[] { internalDns }, dnsConfig.DnsServers);
    }

    [TestMethod]
    public void IncludeAll_falls_back_to_server_dns_when_the_user_resolver_is_not_routable()
    {
        using var ipFilter = new StaticIpFilter(null) {
            ExcludeRanges = new[] { new IpRange(PublicDns) }.ToOrderedList()
        };
        var serverDns = IPAddress.Parse("9.9.9.9");

        var dnsConfig = ClientHelper.GetDnsServers(
            userDnsAddresses: [PublicDns],
            serverDnsAddresses: [serverDns],
            serverIncludeIpRanges: new[] { new IpRange(serverDns) }.ToOrderedList(), // server does not route PublicDns
            ipFilter: ipFilter,
            splitDnsMode: SplitDnsMode.IncludeAll);

        Assert.IsTrue(dnsConfig.IsUserSuppressed, "the user's resolver could not be used at all");
        Assert.AreEqual(DnsSelection.ServerDns, dnsConfig.DnsSelection);
        CollectionAssert.AreEquivalent(new[] { serverDns }, dnsConfig.DnsServers);
    }

    [TestMethod]
    public void IncludeAll_throws_when_no_dns_can_be_tunneled()
    {
        // every candidate is vetoed by the filter and the server routes nothing: under IncludeAll an
        // out-of-tunnel resolver may never be used, so the connect fails loud instead of leaking silently
        using var ipFilter = new StaticIpFilter(null) {
            ExcludeRanges = IpNetwork.All.ToIpRanges()
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => ClientHelper.GetDnsServers(
            userDnsAddresses: [PublicDns],
            serverDnsAddresses: [IPAddress.Parse("9.9.9.9")],
            serverIncludeIpRanges: IpRangeOrderedList.Empty,
            ipFilter: ipFilter,
            splitDnsMode: SplitDnsMode.IncludeAll));
    }

    [TestMethod]
    public void DefaultRoute_falls_back_to_google_outside_the_tunnel_when_nothing_is_routable()
    {
        // same hopeless setup, but DefaultRoute has no in-tunnel promise to keep — the session still gets
        // working DNS, outside the tunnel, and reports it truthfully
        using var ipFilter = new StaticIpFilter(null) {
            ExcludeRanges = IpNetwork.All.ToIpRanges()
        };

        var dnsConfig = ClientHelper.GetDnsServers(
            userDnsAddresses: null,
            serverDnsAddresses: [],
            serverIncludeIpRanges: IpRangeOrderedList.Empty,
            ipFilter: ipFilter,
            splitDnsMode: SplitDnsMode.DefaultRoute);

        Assert.IsFalse(dnsConfig.IsIncludedInVpn);
        Assert.AreEqual(DnsSelection.GoogleDns, dnsConfig.DnsSelection);
        Assert.IsTrue(dnsConfig.DnsServers.Length > 0);
    }
}
