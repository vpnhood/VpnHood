using System.Net;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering.SniExtractors;
using VpnHood.Core.Filtering.DomainFiltering.SniFilteringServices;
using VpnHood.Net.Packets;
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

        protected override bool TryValidateAndExtractPayload(IpPacket ipPacket, out IpEndPointValue flowKey,
            out ReadOnlySpan<byte> payload)
        {
            flowKey = new IpEndPointValue(ipPacket.SourceAddress, 50000);
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
}
