using System.Net;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Test.Tests;

// The stages of a filter pipe that hold no database: the two caches and the two with sets in memory
[TestClass]
public class FilterStagesTest : TestBase
{
    private static IpEndPointValue Ep(string ip, int port = 443) => new(IPAddress.Parse(ip), port);

    // answers what it is told to, and can change its answer in the middle of a lookup, as a stage that swaps its
    // rules while another thread asks it
    private sealed class ScriptedIpFilter : IIpFilter
    {
        public FilterAction Action { get; set; } = FilterAction.Default;
        public FilterAction? ActionAfterNextLookup { get; set; }
        public event EventHandler? Changed;
        public bool IsEmpty => false;

        public FilterAction Process(IpProtocol protocol, IpEndPointValue endPoint)
        {
            var action = Action;
            if (ActionAfterNextLookup != null) {
                Action = ActionAfterNextLookup.Value;
                ActionAfterNextLookup = null;
                Changed?.Invoke(this, EventArgs.Empty);
            }

            return action;
        }

        public Task Reconfigure(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class ScriptedDomainFilter : IDomainFilter
    {
        public FilterAction Action { get; set; } = FilterAction.Default;
        public FilterAction? ActionAfterNextLookup { get; set; }
        public event EventHandler? Changed;
        public bool IsEmpty => false;

        public FilterAction Process(string? domainName)
        {
            var action = Action;
            if (ActionAfterNextLookup != null) {
                Action = ActionAfterNextLookup.Value;
                ActionAfterNextLookup = null;
                Changed?.Invoke(this, EventArgs.Empty);
            }

            return action;
        }

        public Task Reconfigure(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    [TestMethod]
    public void A_cached_verdict_is_its_protocols_own()
    {
        using var filter = new CachedIpFilter(
            new StaticIpFilter(nextFilter: null) { BlockedTcpPorts = [new PortRange(443, 443)] },
            TimeSpan.FromMinutes(1));

        // UDP to the address first: remembered by the destination alone, its verdict answered for TCP too
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Udp, Ep("198.51.100.1")));
        Assert.AreEqual(FilterAction.Block, filter.Process(IpProtocol.Tcp, Ep("198.51.100.1")));
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Udp, Ep("198.51.100.1")));
    }

    [TestMethod]
    public void An_IP_verdict_looked_up_across_a_change_is_not_kept()
    {
        var next = new ScriptedIpFilter { Action = FilterAction.Default, ActionAfterNextLookup = FilterAction.Block };
        using var filter = new CachedIpFilter(next, TimeSpan.FromMinutes(1));

        // the rules change while this lookup is under way: it answers by the old ones, which no later lookup may get
        Assert.AreEqual(FilterAction.Default, filter.Process(IpProtocol.Tcp, Ep("198.51.100.1")));
        Assert.AreEqual(FilterAction.Block, filter.Process(IpProtocol.Tcp, Ep("198.51.100.1")));
    }

    [TestMethod]
    public void A_domain_verdict_looked_up_across_a_change_is_not_kept()
    {
        var next = new ScriptedDomainFilter { Action = FilterAction.Default, ActionAfterNextLookup = FilterAction.Block };
        using var filter = new CachedDomainFilter(next, TimeSpan.FromMinutes(1));

        Assert.AreEqual(FilterAction.Default, filter.Process("example.com"));
        Assert.AreEqual(FilterAction.Block, filter.Process("example.com"));
    }

    [TestMethod]
    public void A_block_stands_whatever_the_next_stage_says()
    {
        // a server puts its own blocks around the filter it is given, and acts on Block alone
        var next = new ScriptedIpFilter();
        using var filter = new StaticIpFilter(next) {
            BlockedRanges = new[] { IpRange.Parse("127.0.0.0-127.255.255.255") }.ToOrderedList(),
            BlockedTcpPorts = [new PortRange(25, 25)]
        };

        foreach (var action in new[] { FilterAction.Include, FilterAction.Exclude, FilterAction.Default }) {
            next.Action = action;
            Assert.AreEqual(FilterAction.Block, filter.Process(IpProtocol.Tcp, Ep("127.33.0.1")), $"Next: {action}");
            Assert.AreEqual(FilterAction.Block, filter.Process(IpProtocol.Tcp, Ep("198.51.100.1", 25)), $"Next: {action}");
        }

        // what it does not block is still the next stage's to decide
        next.Action = FilterAction.Exclude;
        Assert.AreEqual(FilterAction.Exclude, filter.Process(IpProtocol.Tcp, Ep("198.51.100.1")));
    }

    [TestMethod]
    public void A_disposed_stage_leaves_its_next_stages_event()
    {
        // each stage on a next stage that lives on after it
        var ipNext = new StaticIpFilter(nextFilter: null);
        var domainNext = new StaticDomainFilter(nextFilter: null);
        var changedCount = 0;

        var cachedIp = new CachedIpFilter(ipNext, TimeSpan.FromMinutes(1), autoDisposeNextFilter: false);
        var staticIp = new StaticIpFilter(ipNext, autoDisposeNextFilter: false);
        var cachedDomain = new CachedDomainFilter(domainNext, TimeSpan.FromMinutes(1), autoDisposeNextFilter: false);
        var staticDomain = new StaticDomainFilter(domainNext, autoDisposeNextFilter: false);
        cachedIp.Changed += (_, _) => changedCount++;
        staticIp.Changed += (_, _) => changedCount++;
        cachedDomain.Changed += (_, _) => changedCount++;
        staticDomain.Changed += (_, _) => changedCount++;

        // while they live, they pass the next stage's change on
        ipNext.BlockedTcpPorts = [new PortRange(25, 25)];
        domainNext.Blocks = ["example.com"];
        Assert.AreEqual(4, changedCount);

        // disposed, they hear no more of it: the next stage's event held them, and ran them at every change
        cachedIp.Dispose();
        staticIp.Dispose();
        cachedDomain.Dispose();
        staticDomain.Dispose();
        ipNext.BlockedTcpPorts = [];
        domainNext.Blocks = [];
        Assert.AreEqual(4, changedCount);
    }
}
