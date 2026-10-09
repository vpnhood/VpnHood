using System.Net;
using System.Threading.Channels;
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

    // the memory of a packet built over it records the packet's disposal
    private static TestMemoryOwner BuildPacketMemory(int sourcePort)
    {
        using var ipPacket = BuildPacket(sourcePort);
        return new TestMemoryOwner(ipPacket.Buffer.ToArray());
    }

    // a held queue of one, full: one packet held in its batch and one queued
    private async Task FillHeldQueue(TestHeldPacketTransport transport)
    {
        Assert.IsTrue(transport.SendPacketQueued(BuildPacket(1)));
        await AssertEqualsWait(true, () => transport.IsBatchHeld);
        Assert.IsTrue(transport.SendPacketQueued(BuildPacket(2)));
    }

    [TestMethod]
    public async Task Full_queue_drops_its_oldest_packet()
    {
        using var transport = new TestHeldPacketTransport(queueCapacity: 2);

        // one packet held in its batch and two queued: the queue is full
        Assert.IsTrue(transport.SendPacketQueued(BuildPacket(1)));
        await AssertEqualsWait(true, () => transport.IsBatchHeld);
        var oldestMemory = BuildPacketMemory(2);
        Assert.IsTrue(transport.SendPacketQueued(PacketBuilder.Attach(oldestMemory)));
        Assert.IsTrue(transport.SendPacketQueued(BuildPacket(3)));

        // the next takes the place of the oldest waiting packet, which is dropped, disposed and counted, with
        // no wait
        Assert.IsTrue(transport.SendPacketQueued(BuildPacket(4)));
        Assert.AreEqual(1, transport.PacketStat.DroppedPackets);
        Assert.IsTrue(oldestMemory.IsDisposed);

        // moving again, the queue sends what it kept, in order
        transport.Release();
        await AssertEqualsWait(3, () => transport.SentPorts.Count);
        CollectionAssert.AreEqual(new[] { 1, 3, 4 }, transport.SentPorts.ToArray());
    }

    [TestMethod]
    public async Task Blocking_send_waits_for_room()
    {
        using var transport = new TestHeldPacketTransport(queueCapacity: 1, blocking: true);
        await FillHeldQueue(transport);

        // the next send waits until the queue moves, then queues its packet behind the others
        var sendTask = Task.Run(() => transport.SendPacketQueued(BuildPacket(3)), TestCt);
        await Task.Delay(300, TestCt);
        Assert.IsFalse(sendTask.IsCompleted);

        transport.Release();
        Assert.IsTrue(await sendTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt));
        await AssertEqualsWait(3, () => transport.SentPorts.Count);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, transport.SentPorts.ToArray());
    }

    [TestMethod]
    public async Task Blocking_send_is_refused_when_the_transport_is_disposed()
    {
        var transport = new TestHeldPacketTransport(queueCapacity: 1, blocking: true);
        Assert.IsTrue(transport.SendPacketQueued(BuildPacket(1)));
        await AssertEqualsWait(true, () => transport.IsBatchHeld);
        var queuedMemory = BuildPacketMemory(2);
        Assert.IsTrue(transport.SendPacketQueued(PacketBuilder.Attach(queuedMemory)));
        var waitingMemory = BuildPacketMemory(3);
        var sendTask = Task.Run(() => transport.SendPacketQueued(PacketBuilder.Attach(waitingMemory)), TestCt);
        await Task.Delay(300, TestCt);

        // the waiting send ends refused, and neither its packet nor the queued one is left undisposed
        transport.Dispose();
        Assert.IsFalse(await sendTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt));
        Assert.IsTrue(waitingMemory.IsDisposed);
        await AssertEqualsWait(true, () => queuedMemory.IsDisposed);
        Assert.IsFalse(transport.SentPorts.Contains(3));
    }

    [TestMethod]
    public async Task Async_send_waits_for_room_in_blocking_mode()
    {
        using var transport = new TestHeldPacketTransport(queueCapacity: 1, blocking: true);
        await FillHeldQueue(transport);

        var sendTask = transport.SendPacketQueuedAsync(BuildPacket(3)).AsTask();
        await Task.Delay(300, TestCt);
        Assert.IsFalse(sendTask.IsCompleted);

        transport.Release();
        await sendTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        await AssertEqualsWait(3, () => transport.SentPorts.Count);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, transport.SentPorts.ToArray());
    }

    [TestMethod]
    public async Task Async_send_refused_by_a_closed_queue_throws_and_disposes_its_packet()
    {
        var transport = new TestHeldPacketTransport(queueCapacity: 1, blocking: true);
        await FillHeldQueue(transport);
        var waitingMemory = BuildPacketMemory(3);
        var sendTask = transport.SendPacketQueuedAsync(PacketBuilder.Attach(waitingMemory)).AsTask();

        transport.Dispose();
        await Assert.ThrowsExactlyAsync<ChannelClosedException>(() =>
            sendTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt));
        Assert.IsTrue(waitingMemory.IsDisposed);
    }

    [TestMethod]
    public async Task Concurrent_async_passthrough_sends_keep_their_packets()
    {
        using var transport = new TestPassthroughPacketTransport();
        const int taskCount = 8;
        const int packetCount = 2000;
        await Task.WhenAll(Enumerable.Range(0, taskCount).Select(taskIndex => Task.Run(async () => {
            for (var i = 0; i < packetCount; i++)
                await transport.SendPacketQueuedAsync(BuildPacket(taskIndex * packetCount + i + 1));
        }, TestCt)));

        // each packet went exactly once: none was sent in another's place
        var sentPorts = transport.SentPorts;
        Assert.AreEqual(taskCount * packetCount, sentPorts.Count);
        Assert.AreEqual(taskCount * packetCount, sentPorts.Distinct().Count());
    }
}
