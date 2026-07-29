using System.Net;
using VpnHood.Core.Client;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Test.Tests;

// SplitUnsupportedIpMode: the fate of a destination the server does not route when no client split already
// excluded it. ServerIpFilter holds the server's declaration and nothing else — it has no exclude or block
// lists, so no rule of that stage can route traffic around the tunnel behind the mode's back — while the
// client's own verdicts keep their power: Exclude and Block always win, and a forced Include can never push
// traffic onto a server that refused it.
[TestClass]
public class SplitUnsupportedIpModeTest : TestBase
{
    private static readonly IPAddress RoutedIp = IPAddress.Parse("8.8.8.8");
    private static readonly IPAddress UnroutedIp = IPAddress.Parse("130.0.0.10");

    private static FilterAction Process(IIpFilter filter, IPAddress ipAddress)
    {
        return filter.Process(IpProtocol.Udp, new IpEndPointValue(ipAddress, 443));
    }

    // an override lane (domain force-list, ICMP force) as the inner stage would express it
    private sealed class FixedActionFilter(FilterAction action) : IIpFilter
    {
        public event EventHandler? Changed { add { } remove { } }
        public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint) => action;
        public void Reconfigure() { }
        public bool IsEmpty => false;
        public void Dispose() { }
    }

    [TestMethod]
    public void A_server_range_miss_becomes_the_modes_action()
    {
        using var serverIpFilter = new ServerIpFilter(null) {
            IncludeRanges = new[] { new IpRange(RoutedIp) }.ToOrderedList(),
            UnsupportedIpMode = SplitUnsupportedIpMode.Block
        };

        Assert.AreEqual(FilterAction.Default, Process(serverIpFilter, RoutedIp), "a routed destination passes");
        Assert.AreEqual(FilterAction.Block, Process(serverIpFilter, UnroutedIp), "an unrouted one takes the mode's action");

        serverIpFilter.UnsupportedIpMode = SplitUnsupportedIpMode.Exclude;
        Assert.AreEqual(FilterAction.Exclude, Process(serverIpFilter, UnroutedIp), "Exclude mode bypasses instead");
    }

    [TestMethod]
    public void A_client_exclude_wins_over_a_server_block()
    {
        // the user split the destination out; even under Block it bypasses — the mode only owns traffic
        // that WANTED the tunnel
        var clientGates = new StaticIpFilter(null) {
            ExcludeRanges = new[] { new IpRange(UnroutedIp) }.ToOrderedList()
        };
        using var serverIpFilter = new ServerIpFilter(clientGates) {
            IncludeRanges = new[] { new IpRange(RoutedIp) }.ToOrderedList(),
            UnsupportedIpMode = SplitUnsupportedIpMode.Block
        };

        Assert.AreEqual(FilterAction.Exclude, Process(serverIpFilter, UnroutedIp));
    }

    [TestMethod]
    public void A_client_block_stays_a_block()
    {
        var clientGates = new StaticIpFilter(null) {
            BlockedRanges = new[] { new IpRange(RoutedIp) }.ToOrderedList()
        };
        using var serverIpFilter = new ServerIpFilter(clientGates) {
            IncludeRanges = IpNetwork.All.ToIpRanges(),
            UnsupportedIpMode = SplitUnsupportedIpMode.Exclude
        };

        Assert.AreEqual(FilterAction.Block, Process(serverIpFilter, RoutedIp));
    }

    [TestMethod]
    public void A_forced_include_can_not_beat_the_server()
    {
        // an inner Include survives the server's ranges only when the server routes the destination. A
        // refused Include is blocked under EITHER mode: it promised to travel inside the tunnel, so
        // excluding it would leak the very traffic the promise covers.
        using var serverIpFilter = new ServerIpFilter(new FixedActionFilter(FilterAction.Include)) {
            IncludeRanges = new[] { new IpRange(RoutedIp) }.ToOrderedList(),
            UnsupportedIpMode = SplitUnsupportedIpMode.Block
        };

        Assert.AreEqual(FilterAction.Include, Process(serverIpFilter, RoutedIp), "the override lane is preserved on a hit");
        Assert.AreEqual(FilterAction.Block, Process(serverIpFilter, UnroutedIp), "and refused on a miss");

        serverIpFilter.UnsupportedIpMode = SplitUnsupportedIpMode.Exclude;
        Assert.AreEqual(FilterAction.Block, Process(serverIpFilter, UnroutedIp),
            "a refused promise never falls back to Exclude — that would be the leak");
    }

    [TestMethod]
    public void An_unsupported_family_takes_the_v6_modes_action_regardless_of_ranges()
    {
        // a v4-only server may still have declared "no restriction" (All); the family flag must win
        var routedIpV6 = IPAddress.Parse("2001:4860:4860::8888");
        using var serverIpFilter = new ServerIpFilter(null) {
            IncludeRanges = IpNetwork.All.ToIpRanges(),
            UnsupportedIpMode = SplitUnsupportedIpMode.Exclude,
            UnsupportedIpV6Mode = SplitUnsupportedIpMode.Block,
            IsIpV6SupportedByServer = false
        };

        Assert.AreEqual(FilterAction.Default, Process(serverIpFilter, RoutedIp), "IPv4 is untouched");
        Assert.AreEqual(FilterAction.Block, Process(serverIpFilter, routedIpV6), "IPv6 takes ITS mode, not the general one");

        serverIpFilter.UnsupportedIpV6Mode = SplitUnsupportedIpMode.Exclude;
        Assert.AreEqual(FilterAction.Exclude, Process(serverIpFilter, routedIpV6), "Exclude bypasses instead");

        serverIpFilter.IsIpV6SupportedByServer = true;
        Assert.AreEqual(FilterAction.Default, Process(serverIpFilter, routedIpV6),
            "a supported family is judged by the ranges again");
    }

    [TestMethod]
    public void A_v6_miss_inside_a_supported_family_takes_the_general_mode()
    {
        // the v6 mode owns only the family-unsupported case; narrow v6 ranges are ordinary misses
        var unroutedIpV6 = IPAddress.Parse("2001:db8::1");
        using var serverIpFilter = new ServerIpFilter(null) {
            IncludeRanges = new[] { new IpRange(RoutedIp) }.ToOrderedList(),
            UnsupportedIpMode = SplitUnsupportedIpMode.Exclude,
            UnsupportedIpV6Mode = SplitUnsupportedIpMode.Block,
            IsIpV6SupportedByServer = true
        };

        Assert.AreEqual(FilterAction.Exclude, Process(serverIpFilter, unroutedIpV6));
    }

    [TestMethod]
    public void The_capture_set_follows_the_modes_per_family()
    {
        // the modes' other half: the filter decides the fate of a captured miss, but the capture set
        // decides what the OS hands over at all — Block must claim a miss to kill it, Exclude leaves
        // it to the OS, and a family the server cannot carry follows its own mode
        var hostIp = IPAddress.Parse("51.81.81.250");
        var outOfTunnelDns = new DnsConfig {
            DnsSelection = DnsSelection.UserDns,
            IsIncludedInVpn = false,
            IsUserSuppressed = false,
            DnsServers = []
        };
        var serverRanges = new[] { IpRange.Parse("1.0.0.0-1.0.0.255") }.ToOrderedList();
        var globalV6 = IPAddress.Parse("2600::1");

        IpRangeOrderedList Build(SplitUnsupportedIpMode mode, SplitUnsupportedIpMode v6Mode, bool serverV6) =>
            ClientHelper.BuildIncludeIpRangesByDevice(
                clientIncludeIpRanges: IpNetwork.All.ToIpRanges(),
                serverIncludeIpRanges: serverRanges,
                unsupportedIpMode: mode,
                unsupportedIpV6Mode: v6Mode,
                isIpV6SupportedByServer: serverV6,
                includeLocalNetwork: false,
                hostIpAddress: hostIp,
                dnsConfig: outOfTunnelDns);

        // Exclude claims only what the server routes — every miss is left to the OS (bypass)
        var capture = Build(SplitUnsupportedIpMode.Exclude, SplitUnsupportedIpMode.Exclude, serverV6: false);
        Assert.IsTrue(capture.Contains(IPAddress.Parse("1.0.0.10")));
        Assert.IsFalse(capture.Contains(IPAddress.Parse("2.0.0.10")), "an unrouted destination stays with the OS");
        Assert.IsFalse(capture.Contains(globalV6), "an unsupported family stays with the OS");

        // Block ignores the server's declaration: a miss must be CAPTURED to be killed
        capture = Build(SplitUnsupportedIpMode.Block, SplitUnsupportedIpMode.Exclude, serverV6: false);
        Assert.IsTrue(capture.Contains(IPAddress.Parse("2.0.0.10")), "Block captures the miss to kill it");
        Assert.IsFalse(capture.Contains(globalV6), "the unsupported family keeps ITS OWN mode");

        // the unsupported family under Block is claimed so it dies inside instead of leaking outside
        capture = Build(SplitUnsupportedIpMode.Exclude, SplitUnsupportedIpMode.Block, serverV6: false);
        Assert.IsTrue(capture.Contains(globalV6));
        Assert.IsFalse(capture.Contains(IPAddress.Parse("2.0.0.10")));

        // a SUPPORTED v6 follows the general mode; the v6 mode has no say
        capture = Build(SplitUnsupportedIpMode.Exclude, SplitUnsupportedIpMode.Block, serverV6: true);
        Assert.IsFalse(capture.Contains(globalV6), "v6 misses of a v6-capable server follow the general mode");
    }

    [TestMethod]
    public void An_empty_declaration_means_no_restriction()
    {
        // "no declaration yet" and "server routes everything" are one honest state: All. An empty
        // assignment is converted at the door, so the stage can never accidentally turn every address
        // into a miss.
        using var serverIpFilter = new ServerIpFilter(null) {
            UnsupportedIpMode = SplitUnsupportedIpMode.Block
        };
        Assert.AreEqual(FilterAction.Default, Process(serverIpFilter, RoutedIp), "the default is All");

        serverIpFilter.IncludeRanges = IpRangeOrderedList.Empty;
        Assert.IsTrue(serverIpFilter.IncludeRanges.IsAll(), "an empty declaration is converted to All");
        Assert.AreEqual(FilterAction.Default, Process(serverIpFilter, UnroutedIp));
    }
}
