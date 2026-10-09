using System.Collections.Concurrent;
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

    // a send that tells a refusal by false, sync or async
    private static async Task<bool> SendPacket(TestHeldPacketTransport transport, IpPacket ipPacket, bool isAsync)
    {
        if (!isAsync)
            return transport.SendPacketQueued(ipPacket);

        try {
            await transport.SendPacketQueuedAsync(ipPacket);
            return true;
        }
        catch (ChannelClosedException) {
            return false;
        }
    }

    [TestMethod]
    public async Task Full_queue_drops_its_oldest_packet()
    {
        using var transport = new TestHeldPacketTransport(queueCapacity: 2);

        // one packet held in its batch and two queued: the queue is full
        Assert.IsTrue(transport.SendPacketQueued(BuildPacket(1)));
        await AssertEqualsWait(true, () => transport.IsBatchHeld);
        var oldestMemory = BuildPacketMemory(2);
        var oldestPacket = PacketBuilder.Attach(oldestMemory);
        Assert.IsTrue(transport.SendPacketQueued(oldestPacket));
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
        GC.KeepAlive(oldestPacket);
    }

    [TestMethod]
    public async Task Failing_logger_does_not_fail_a_write_that_dropped_the_oldest()
    {
        using var transport = new TestHeldPacketTransport(queueCapacity: 1);
        await FillHeldQueue(transport);
        transport.FailDropLog = true;

        Assert.IsTrue(transport.SendPacketQueued(BuildPacket(3)));
        Assert.AreEqual(1, transport.PacketStat.DroppedPackets);
    }

    [TestMethod]
    public async Task Blocking_send_waits_for_room()
    {
        using var transport = new TestHeldPacketTransport(queueCapacity: 1, blocking: true);
        await FillHeldQueue(transport);

        // the next send waits until the queue moves, then queues its packet behind the others
        var sendTask = Task.Run(() => transport.SendPacketQueued(BuildPacket(3)), TestCt);
        await AssertEqualsWait(3, () => transport.SendsStarted);
        await Task.Delay(100, TestCt);
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
        var queuedPacket = PacketBuilder.Attach(queuedMemory);
        Assert.IsTrue(transport.SendPacketQueued(queuedPacket));
        var waitingMemory = BuildPacketMemory(3);
        var waitingPacket = PacketBuilder.Attach(waitingMemory);
        var sendTask = Task.Run(() => transport.SendPacketQueued(waitingPacket), TestCt);
        await AssertEqualsWait(3, () => transport.SendsStarted);

        // the send past its disposed check is refused, waiting for room yet or not, and neither its packet nor
        // the queued one is left undisposed
        transport.Dispose();
        Assert.IsFalse(await sendTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt));
        Assert.IsTrue(waitingMemory.IsDisposed);
        await AssertEqualsWait(true, () => queuedMemory.IsDisposed);
        Assert.IsFalse(transport.SentPorts.Contains(3));
        GC.KeepAlive(queuedPacket);
        GC.KeepAlive(waitingPacket);
    }

    [TestMethod]
    public async Task Async_send_waits_for_room_in_blocking_mode()
    {
        using var transport = new TestHeldPacketTransport(queueCapacity: 1, blocking: true);
        await FillHeldQueue(transport);

        var sendTask = transport.SendPacketQueuedAsync(BuildPacket(3)).AsTask();
        await Task.Delay(100, TestCt);
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
        var waitingPacket = PacketBuilder.Attach(waitingMemory);
        var sendTask = transport.SendPacketQueuedAsync(waitingPacket).AsTask();

        transport.Dispose();
        await Assert.ThrowsExactlyAsync<ChannelClosedException>(() =>
            sendTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt));
        Assert.IsTrue(waitingMemory.IsDisposed);
        GC.KeepAlive(waitingPacket);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task Send_refused_by_a_closed_queue_disposes_its_packet(bool blocking, bool isAsync)
    {
        // a transport that hands its packets on: only the rule for an undelivered packet disposes this one
        var transport = new TestHeldPacketTransport(queueCapacity: 1, blocking: blocking, autoDisposePackets: false);
        using var sendGate = new ManualResetEventSlim(false);
        transport.SendGate = sendGate;

        // a send held past its disposed check, and the transport disposed under it
        var memory = BuildPacketMemory(1);
        var ipPacket = PacketBuilder.Attach(memory);
        var sendTask = Task.Run(() => SendPacket(transport, ipPacket, isAsync), TestCt);
        await AssertEqualsWait(1, () => transport.SendsStarted);
        transport.Dispose();
        sendGate.Set();

        Assert.IsFalse(await sendTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt));
        Assert.IsTrue(memory.IsDisposed);
        GC.KeepAlive(ipPacket);
    }

    [TestMethod]
    public async Task Send_to_a_disposed_transport_throws_and_disposes_its_packet()
    {
        var transport = new TestHeldPacketTransport(queueCapacity: 1);
        transport.Dispose();

        var memory = BuildPacketMemory(1);
        var ipPacket = PacketBuilder.Attach(memory);
        Assert.ThrowsExactly<ObjectDisposedException>(() => transport.SendPacketQueued(ipPacket));
        Assert.IsTrue(memory.IsDisposed);

        var asyncMemory = BuildPacketMemory(2);
        var asyncPacket = PacketBuilder.Attach(asyncMemory);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
            transport.SendPacketQueuedAsync(asyncPacket).AsTask());
        Assert.IsTrue(asyncMemory.IsDisposed);
        GC.KeepAlive(ipPacket);
        GC.KeepAlive(asyncPacket);
    }

    [TestMethod]
    public async Task Transport_that_hands_packets_on_disposes_only_what_it_does_not_deliver()
    {
        var transport = new TestHeldPacketTransport(queueCapacity: 1, autoDisposePackets: false);

        // delivered: the one held in its batch; dropped: the oldest waiting when the queue is full; left: the
        // last one queued when the transport is disposed
        var deliveredMemory = BuildPacketMemory(1);
        var deliveredPacket = PacketBuilder.Attach(deliveredMemory);
        Assert.IsTrue(transport.SendPacketQueued(deliveredPacket));
        await AssertEqualsWait(true, () => transport.IsBatchHeld);
        var droppedMemory = BuildPacketMemory(2);
        var droppedPacket = PacketBuilder.Attach(droppedMemory);
        Assert.IsTrue(transport.SendPacketQueued(droppedPacket));
        var leftMemory = BuildPacketMemory(3);
        var leftPacket = PacketBuilder.Attach(leftMemory);
        Assert.IsTrue(transport.SendPacketQueued(leftPacket));
        Assert.IsTrue(droppedMemory.IsDisposed);

        transport.Dispose();
        await AssertEqualsWait(true, () => leftMemory.IsDisposed);
        await AssertEqualsWait(1, () => transport.SentPorts.Count);
        Assert.IsFalse(deliveredMemory.IsDisposed);
        GC.KeepAlive(deliveredPacket);
        GC.KeepAlive(droppedPacket);
        GC.KeepAlive(leftPacket);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Sends_racing_a_dispose_leave_no_packet_undisposed(bool blocking)
    {
        var transport = new TestHeldPacketTransport(queueCapacity: 2, blocking: blocking);
        transport.Release();

        // senders that send until the transport refuses them, while it is disposed; the packets are held, so
        // only the transport disposes one, not a finalizer
        var memories = new ConcurrentQueue<TestMemoryOwner>();
        var packets = new ConcurrentQueue<IpPacket>();
        var senders = Enumerable.Range(0, 8).Select(_ => Task.Run(() => {
            for (var i = 0; ; i++) {
                var memory = BuildPacketMemory(1 + i % 60_000);
                var ipPacket = PacketBuilder.Attach(memory);
                memories.Enqueue(memory);
                packets.Enqueue(ipPacket);
                try {
                    transport.SendPacketQueued(ipPacket);
                }
                catch (ObjectDisposedException) {
                    return;
                }
            }
        }, TestCt)).ToArray();

        await Task.Delay(50, TestCt);
        transport.Dispose();
        await Task.WhenAll(senders).WaitAsync(TimeSpan.FromSeconds(10), TestCt);

        // sent, dropped, refused or left in the queue, every packet ends disposed
        await AssertEqualsWait(0, () => memories.Count(memory => !memory.IsDisposed));
        Assert.AreEqual(0, transport.QueueLength);
        GC.KeepAlive(packets);
    }

    [TestMethod]
    public void Second_dispose_does_nothing()
    {
        var transport = new TestHeldPacketTransport(queueCapacity: 1);
        transport.Dispose();
        transport.Dispose();

        Assert.AreEqual(1, transport.PreDisposeCount);
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

    [TestMethod]
    public void Passthrough_send_that_does_not_complete_at_once_is_waited_out_before_the_error()
    {
        using var transport = new TestDelayedPassthroughPacketTransport(TimeSpan.FromMilliseconds(200));

        Assert.ThrowsExactly<InvalidOperationException>(() => transport.SendPacketQueued(BuildPacket(1)));
        Assert.IsTrue(transport.IsSendFinished);
    }
}
