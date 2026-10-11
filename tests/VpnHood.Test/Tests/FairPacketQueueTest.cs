using System.Collections.Concurrent;
using System.Net;
using VpnHood.Net.Packets;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

// The lane of a UDP packet to port 53 follows its source port modulo 64: 1000, 1064, 1128... share one lane,
// and 2000 has another
[TestClass]
public class FairPacketQueueTest : TestBase
{
    private static IpPacket BuildPacket(int sourcePort)
    {
        return PacketBuilder.BuildUdp(IPAddress.Parse("10.0.0.2"), IPAddress.Parse("10.0.0.3"), sourcePort, 53,
            new byte[16]);
    }

    // the memory of a packet built over it records the packet's disposal
    private static TestMemoryOwner BuildPacketMemory(int sourcePort)
    {
        using var ipPacket = BuildPacket(sourcePort);
        return new TestMemoryOwner(ipPacket.Buffer.ToArray());
    }

    // one packet taken into a held turn, so the next ones wait in the queue
    private async Task HoldTurn(TestHeldFairPacketQueue queue, int sourcePort)
    {
        Assert.IsTrue(queue.SendPacketQueued(BuildPacket(sourcePort)));
        await AssertEqualsWait(1, () => queue.TurnsStarted);
    }

    [TestMethod]
    public async Task Flood_fills_only_its_own_lane()
    {
        using var queue = new TestHeldFairPacketQueue(capacity: 4);
        await HoldTurn(queue, 1000);

        // a flood that fills the queue and has had its turns
        var oldestMemory = BuildPacketMemory(1192);
        var oldestPacket = PacketBuilder.Attach(oldestMemory);
        Assert.IsTrue(queue.SendPacketQueued(BuildPacket(1064)));
        Assert.IsTrue(queue.SendPacketQueued(BuildPacket(1128)));
        Assert.IsTrue(queue.SendPacketQueued(oldestPacket));
        Assert.IsTrue(queue.SendPacketQueued(BuildPacket(1256)));
        queue.AllowTurns(2);
        await AssertEqualsWait(3, () => queue.TurnsStarted);
        Assert.IsTrue(queue.SendPacketQueued(BuildPacket(1320)));
        Assert.IsTrue(queue.SendPacketQueued(BuildPacket(1384)));
        Assert.AreEqual(0, queue.DroppedPackets);

        // a packet of another flow takes the place of the flood's oldest, and goes next
        Assert.IsTrue(queue.SendPacketQueued(BuildPacket(2000)));
        Assert.AreEqual(1, queue.DroppedPackets);
        Assert.IsTrue(oldestMemory.IsDisposed);

        queue.Release();
        await AssertEqualsWait(7, () => queue.SentPorts.Count);
        CollectionAssert.AreEqual(new[] { 1000, 1064, 1128, 2000, 1256, 1320, 1384 }, queue.SentPorts.ToArray());
        GC.KeepAlive(oldestPacket);
    }

    [TestMethod]
    public async Task One_flow_alone_uses_the_whole_queue()
    {
        using var queue = new TestHeldFairPacketQueue(capacity: 4);
        await HoldTurn(queue, 1000);

        foreach (var sourcePort in new[] { 1064, 1128, 1192, 1256 })
            Assert.IsTrue(queue.SendPacketQueued(BuildPacket(sourcePort)));
        Assert.AreEqual(0, queue.DroppedPackets);

        // past the capacity, the flow drops its own oldest
        Assert.IsTrue(queue.SendPacketQueued(BuildPacket(1320)));
        Assert.AreEqual(1, queue.DroppedPackets);

        queue.Release();
        await AssertEqualsWait(5, () => queue.SentPorts.Count);
        CollectionAssert.AreEqual(new[] { 1000, 1128, 1192, 1256, 1320 }, queue.SentPorts.ToArray());
    }

    [TestMethod]
    public async Task New_lanes_cannot_starve_a_busy_one()
    {
        using var queue = new TestHeldFairPacketQueue(capacity: 8);
        await HoldTurn(queue, 1000);
        foreach (var sourcePort in new[] { 1064, 1128, 1192 })
            Assert.IsTrue(queue.SendPacketQueued(BuildPacket(sourcePort)));

        // the flow's lane has had its turn, so it is no longer new when three new flows come
        queue.AllowTurns(1);
        await AssertEqualsWait(2, () => queue.TurnsStarted);
        foreach (var sourcePort in new[] { 2000, 2001, 2002 })
            Assert.IsTrue(queue.SendPacketQueued(BuildPacket(sourcePort)));

        // the busy lane still gets every other turn
        queue.Release();
        await AssertEqualsWait(7, () => queue.SentPorts.Count);
        var sentPorts = queue.SentPorts;
        CollectionAssert.AreEqual(new[] { 1000, 1064, 1128 }, sentPorts.Take(3).ToArray());
        Assert.AreEqual(1192, sentPorts[4]);
        CollectionAssert.AreEquivalent(new[] { 2000, 2001, 2002 }, new[] { sentPorts[3], sentPorts[5], sentPorts[6] });
    }

    [TestMethod]
    public async Task Full_queue_breaks_a_tie_against_the_arriving_lane()
    {
        using var queue = new TestHeldFairPacketQueue(capacity: 2);
        await HoldTurn(queue, 5000);
        var tiedMemory = BuildPacketMemory(2000);
        var tiedPacket = PacketBuilder.Attach(tiedMemory);
        Assert.IsTrue(queue.SendPacketQueued(tiedPacket));
        Assert.IsTrue(queue.SendPacketQueued(BuildPacket(1064)));

        // two lanes of one packet each: the arriving packet's lane makes the room
        Assert.IsTrue(queue.SendPacketQueued(BuildPacket(2064)));
        Assert.IsTrue(tiedMemory.IsDisposed);

        queue.Release();
        await AssertEqualsWait(3, () => queue.SentPorts.Count);
        CollectionAssert.AreEquivalent(new[] { 5000, 1064, 2064 }, queue.SentPorts.ToArray());
        GC.KeepAlive(tiedPacket);
    }

    [TestMethod]
    public async Task Fragments_share_one_lane()
    {
        using var queue = new TestHeldFairPacketQueue(capacity: 3);
        await HoldTurn(queue, 5000);

        // fragments of different ports, which their ports would spread over three lanes
        var fragmentMemories = new[] { 1000, 1001, 1002 }.Select(BuildPacketMemory).ToArray();
        var fragments = fragmentMemories.Select(memory => (IpV4Packet)PacketBuilder.Attach(memory)).ToArray();
        foreach (var fragment in fragments) {
            fragment.MoreFragments = true;
            Assert.IsTrue(queue.SendPacketQueued(fragment));
        }

        // their one lane is the longest, so it drops its oldest
        Assert.IsTrue(queue.SendPacketQueued(BuildPacket(2000)));
        Assert.IsTrue(fragmentMemories[0].IsDisposed);

        queue.Release();
        await AssertEqualsWait(4, () => queue.SentPorts.Count);
        var sentPorts = queue.SentPorts.ToList();
        Assert.IsLessThan(sentPorts.IndexOf(1002), sentPorts.IndexOf(1001));
        CollectionAssert.Contains(sentPorts, 2000);
        GC.KeepAlive(fragments);
    }

    [TestMethod]
    public async Task Idle_queue_wakes_for_each_packet()
    {
        using var queue = new TestHeldFairPacketQueue(capacity: 4);
        queue.Release();

        for (var i = 1; i <= 3; i++) {
            Assert.IsTrue(queue.SendPacketQueued(BuildPacket(1000 + i)));
            await AssertEqualsWait(i, () => queue.SentPorts.Count);
        }
    }

    [TestMethod]
    public async Task Failed_turn_drops_its_packet_and_the_queue_goes_on()
    {
        using var queue = new TestHeldFairPacketQueue(capacity: 4) { FailSendPort = 1000 };
        queue.Release();

        var failedMemory = BuildPacketMemory(1000);
        var failedPacket = PacketBuilder.Attach(failedMemory);
        Assert.IsTrue(queue.SendPacketQueued(failedPacket));
        await AssertEqualsWait(1L, () => queue.DroppedPackets);
        Assert.IsTrue(failedMemory.IsDisposed);

        Assert.IsTrue(queue.SendPacketQueued(BuildPacket(2000)));
        await AssertEqualsWait(1, () => queue.SentPorts.Count);
        Assert.AreEqual(2000, queue.SentPorts[0]);
        GC.KeepAlive(failedPacket);
    }

    [TestMethod]
    public async Task Faulted_queue_drops_what_it_holds_and_starts_afresh()
    {
        // a fault outside a turn, which only a bug can cause: here a failed turn whose packet then fails to dispose
        using var queue = new TestHeldFairPacketQueue(capacity: 4) { FailSendPort = 1000 };
        var faultyMemory = BuildPacketMemory(1000);
        faultyMemory.FailsFirstDispose = true;
        var faultyPacket = PacketBuilder.Attach(faultyMemory);
        Assert.IsTrue(queue.SendPacketQueued(faultyPacket));
        await AssertEqualsWait(1, () => queue.TurnsStarted);
        var heldMemory = BuildPacketMemory(2000);
        var heldPacket = PacketBuilder.Attach(heldMemory);
        Assert.IsTrue(queue.SendPacketQueued(heldPacket));

        // the packet held goes too, and the queue goes on
        queue.Release();
        await AssertEqualsWait(2L, () => queue.DroppedPackets);
        Assert.IsTrue(heldMemory.IsDisposed);
        Assert.AreEqual(0, queue.QueueLength);
        Assert.IsTrue(queue.SendPacketQueued(BuildPacket(3000)));
        await AssertEqualsWait(1, () => queue.SentPorts.Count);
        Assert.AreEqual(3000, queue.SentPorts[0]);

        // its memory throws only once, so no finalizer meets the fault
        faultyPacket.Dispose();
        GC.KeepAlive(heldPacket);
    }

    [TestMethod]
    public async Task Disposed_queue_drops_what_it_holds_and_refuses_later_sends()
    {
        var queue = new TestHeldFairPacketQueue(capacity: 4);
        await HoldTurn(queue, 1000);
        var queuedMemory = BuildPacketMemory(2000);
        var queuedPacket = PacketBuilder.Attach(queuedMemory);
        Assert.IsTrue(queue.SendPacketQueued(queuedPacket));

        queue.Dispose();
        Assert.IsTrue(queuedMemory.IsDisposed);
        Assert.AreEqual(0, queue.QueueLength);

        var refusedMemory = BuildPacketMemory(3000);
        var refusedPacket = PacketBuilder.Attach(refusedMemory);
        Assert.IsFalse(queue.SendPacketQueued(refusedPacket));
        Assert.IsTrue(refusedMemory.IsDisposed);
        Assert.AreEqual(2, queue.DroppedPackets);

        // the packet in its turn is the turn's, and still goes
        queue.Release();
        await AssertEqualsWait(1, () => queue.SentPorts.Count);
        GC.KeepAlive(queuedPacket);
        GC.KeepAlive(refusedPacket);
    }

    [TestMethod]
    public async Task Sends_racing_a_dispose_leave_no_packet_undisposed()
    {
        var queue = new TestHeldFairPacketQueue(capacity: 8);
        queue.Release();

        // senders that send until the queue refuses them, while it is disposed; the packets are held, so only
        // the queue or its turns dispose one, not a finalizer
        var memories = new ConcurrentQueue<TestMemoryOwner>();
        var packets = new ConcurrentQueue<IpPacket>();
        var senders = Enumerable.Range(0, 8).Select(_ => Task.Run(() => {
            for (var i = 0; ; i++) {
                var memory = BuildPacketMemory(1 + i % 60_000);
                var ipPacket = PacketBuilder.Attach(memory);
                memories.Enqueue(memory);
                packets.Enqueue(ipPacket);
                if (!queue.SendPacketQueued(ipPacket))
                    return;
            }
        }, TestCt)).ToArray();

        // disposed while every sender is under way
        await AssertEqualsWait(true, () => memories.Count >= 2000);
        queue.Dispose();
        await Task.WhenAll(senders).WaitAsync(TimeSpan.FromSeconds(10), TestCt);

        // sent, dropped, refused or left in the queue, every packet ends disposed
        await AssertEqualsWait(0, () => memories.Count(memory => !memory.IsDisposed));
        Assert.AreEqual(0, queue.QueueLength);
        GC.KeepAlive(packets);
    }
}
