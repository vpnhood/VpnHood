using System.Net;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering.SniExtractors;
using VpnHood.Core.Filtering.DomainFiltering.SniFilteringServices;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Test.Tests;

[TestClass]
public class PacketSniFilteringServiceTest : TestBase
{
    // asks for more on a flow's first packet and gives up on the next, as the QUIC extractor does past its packet budget
    // or its deadline
    private sealed class GivingUpSniFilteringService()
        : PacketSniFilteringService(new StaticDomainFilter(nextFilter: null), TimeSpan.FromMinutes(1), sniEventId: null)
    {
        private int _packetCount;
        protected override string ProtocolName => "Test";

        protected override bool TryValidateAndExtractPayload(IpPacket ipPacket, out FlowKey flowKey,
            out ReadOnlySpan<byte> payload)
        {
            flowKey = new FlowKey(new IpEndPointValue(ipPacket.SourceAddress, 50000), default);
            payload = default;
            return true;
        }

        protected override PacketSniResult ExtractSni(ReadOnlySpan<byte> payload, object? state, long nowTicks)
        {
            return ++_packetCount == 1 ? PacketSniResult.Pending(new object()) : PacketSniResult.NotFound;
        }
    }

    private static IpPacket BuildPacket()
    {
        return PacketBuilder.BuildUdp(IPAddress.Parse("10.0.0.2"), IPAddress.Parse("10.0.0.3"),
            sourcePort: 50000, destinationPort: 443, payload: new byte[16]);
    }

    [TestMethod]
    public void A_flow_given_up_releases_the_packets_it_buffered()
    {
        using var service = new GivingUpSniFilteringService();
        using var firstPacket = BuildPacket();
        Assert.IsTrue(service.ProcessPacket(firstPacket).NeedMore);

        using var secondPacket = BuildPacket();
        var result = service.ProcessPacket(secondPacket);
        CollectionAssert.AreEqual(new[] { firstPacket }, result.Packets.ToArray());
    }

    private static IpPacket BuildTcp(ReadOnlySpan<byte> payload, bool finish = false, bool reset = false)
    {
        var ipPacket = PacketBuilder.BuildTcp(IPAddress.Parse("10.0.0.2"), IPAddress.Parse("198.51.100.1"),
            sourcePort: 50000, destinationPort: 443, options: [], payload: payload);
        var tcpPacket = ipPacket.ExtractTcp();
        tcpPacket.Finish = finish;
        tcpPacket.Reset = reset;
        return ipPacket;
    }

    private static TcpSniFilteringService CreateTcpService()
    {
        return new TcpSniFilteringService(new StaticDomainFilter(nextFilter: null) {
            Blocks = ["blocked.example"]
        }, TimeSpan.FromMinutes(1), sniEventId: null);
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void A_TCP_flow_ends_with_a_FIN_or_RST_that_carries_no_data(bool finish, bool reset)
    {
        // the end was passed over, as a packet with no data, before the step that ends the flow, so its verdict answered
        // the next connection of the same addresses and ports, whose name was never read
        using var service = CreateTcpService();
        using var blockedHello = BuildTcp(DomainFilteringServiceTest.BuildClientHello("www.blocked.example"));
        Assert.AreEqual(FilterAction.Block, service.ProcessPacket(blockedHello).Action);

        using var end = BuildTcp([], finish: finish, reset: reset);
        Assert.AreEqual(FilterAction.Block, service.ProcessPacket(end).Action, "the end is the flow's");

        using var allowedHello = BuildTcp(DomainFilteringServiceTest.BuildClientHello("www.allowed.example"));
        var result = service.ProcessPacket(allowedHello);
        Assert.AreEqual("www.allowed.example", result.DomainName);
        Assert.AreEqual(FilterAction.Default, result.Action);
    }

    [TestMethod]
    public void A_ClientHello_carried_with_the_FIN_leaves_no_verdict_for_the_next_connection()
    {
        // the allowed name's flow was kept after its end, so the next connection of the same addresses and ports took
        // its verdict, and its blocked name was never read
        using var service = CreateTcpService();
        using var allowedHelloAndEnd =
            BuildTcp(DomainFilteringServiceTest.BuildClientHello("www.allowed.example"), finish: true);
        Assert.AreEqual(FilterAction.Default, service.ProcessPacket(allowedHelloAndEnd).Action);

        using var blockedHello = BuildTcp(DomainFilteringServiceTest.BuildClientHello("www.blocked.example"));
        var result = service.ProcessPacket(blockedHello);
        Assert.AreEqual("www.blocked.example", result.DomainName);
        Assert.AreEqual(FilterAction.Block, result.Action);
    }

    [TestMethod]
    public void A_ClientHello_that_its_flow_ends_before_it_is_complete_is_dropped()
    {
        // the part held for the rest of the name goes with the end, never as no objection: the name may be in the rest
        using var service = CreateTcpService();
        using var partialHello = BuildTcp(DomainFilteringServiceTest.BuildClientHello("www.blocked.example")[..40]);
        Assert.IsTrue(service.ProcessPacket(partialHello).NeedMore);

        using var end = BuildTcp([], finish: true);
        var result = service.ProcessPacket(end);
        Assert.AreEqual(FilterAction.Block, result.Action);
        Assert.AreSame(partialHello, result.Packets.Single());
    }
}
