using System.Net;
using VpnHood.Net.Packets;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class PacketTransportTest : TestBase
{
    private static IpPacket BuildPacket(int sourcePort)
    {
        return PacketBuilder.BuildUdp(IPAddress.Parse("10.0.0.2"), IPAddress.Parse("10.0.0.3"), sourcePort, 53,
            new byte[16]);
    }

    [TestMethod]
    public async Task Full_queue_drops_its_oldest_packet()
    {
        using var transport = new TestHeldPacketTransport(queueCapacity: 2);

        // one packet held in its batch and two queued: the queue is full
        Assert.IsTrue(transport.SendPacketQueued(BuildPacket(1)));
        await AssertEqualsWait(true, () => transport.IsBatchHeld);
        Assert.IsTrue(transport.SendPacketQueued(BuildPacket(2)));
        Assert.IsTrue(transport.SendPacketQueued(BuildPacket(3)));

        // the next takes the place of the oldest waiting packet, which is dropped and counted, with no wait
        Assert.IsTrue(transport.SendPacketQueued(BuildPacket(4)));
        Assert.AreEqual(1, transport.PacketStat.DroppedPackets);

        // moving again, the queue sends what it kept, in order
        transport.Release();
        await AssertEqualsWait(3, () => transport.SentPorts.Count);
        CollectionAssert.AreEqual(new[] { 1, 3, 4 }, transport.SentPorts.ToArray());
    }
}
