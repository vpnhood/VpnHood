using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
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
    public async Task Blocking_send_drops_its_packet_after_its_timeout()
    {
        using var transport = new TestHeldPacketTransport(queueCapacity: 1, blocking: true,
            blockingTimeout: TimeSpan.FromMilliseconds(200));
        await FillHeldQueue(transport);

        // the queue does not move: the send gives up once its timeout is out, and drops its packet
        var memory = BuildPacketMemory(3);
        var ipPacket = PacketBuilder.Attach(memory);
        var stopwatch = Stopwatch.StartNew();
        Assert.IsFalse(await Task.Run(() => transport.SendPacketQueued(ipPacket), TestCt)
            .WaitAsync(TimeSpan.FromSeconds(5), TestCt));
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150), stopwatch.Elapsed);
        Assert.IsTrue(memory.IsDisposed);
        Assert.AreEqual(1, transport.PacketStat.DroppedPackets);

        // moving again, the queue sends what it kept
        transport.Release();
        await AssertEqualsWait(2, () => transport.SentPorts.Count);
        CollectionAssert.AreEqual(new[] { 1, 2 }, transport.SentPorts.ToArray());
        GC.KeepAlive(ipPacket);
    }

    [TestMethod]
    public async Task Async_blocking_send_drops_its_packet_after_its_timeout()
    {
        using var transport = new TestHeldPacketTransport(queueCapacity: 1, blocking: true,
            blockingTimeout: TimeSpan.FromMilliseconds(200));
        await FillHeldQueue(transport);

        // the queue does not move: the send gives up once its timeout is out, drops its packet and completes
        var memory = BuildPacketMemory(3);
        var ipPacket = PacketBuilder.Attach(memory);
        var stopwatch = Stopwatch.StartNew();
        await transport.SendPacketQueuedAsync(ipPacket).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestCt);
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150), stopwatch.Elapsed);
        Assert.IsTrue(memory.IsDisposed);
        Assert.AreEqual(1, transport.PacketStat.DroppedPackets);

        // moving again, the queue sends what it kept
        transport.Release();
        await AssertEqualsWait(2, () => transport.SentPorts.Count);
        CollectionAssert.AreEqual(new[] { 1, 2 }, transport.SentPorts.ToArray());
        GC.KeepAlive(ipPacket);
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
        Assert.AreEqual(1, transport.PacketStat.DroppedPackets);
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
        await AssertEqualsWait(2L, () => transport.PacketStat.DroppedPackets);
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

        // disposed while every sender is under way
        await AssertEqualsWait(true, () => memories.Count >= 2000);
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
    public async Task Passthrough_send_that_does_not_complete_at_once_throws_and_keeps_to_its_own_packet()
    {
        using var transport = new TestDelayedPassthroughPacketTransport();

        // the error comes at once, with no wait for the send
        var delayedMemory = BuildPacketMemory(1);
        var delayedPacket = PacketBuilder.Attach(delayedMemory);
        var sendTask = Task.Run(() => transport.SendPacketQueued(delayedPacket), TestCt);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            sendTask.WaitAsync(TimeSpan.FromSeconds(5), TestCt));

        // the next sender's packet goes, and is disposed, by its own send
        var nextMemory = BuildPacketMemory(2);
        var nextPacket = PacketBuilder.Attach(nextMemory);
        Assert.IsTrue(transport.SendPacketQueued(nextPacket));
        Assert.IsTrue(nextMemory.IsDisposed);

        // the first send ends with its own packet, not the next one's
        Assert.IsFalse(delayedMemory.IsDisposed);
        transport.Release();
        await AssertEqualsWait(true, () => delayedMemory.IsDisposed);
        await AssertEqualsWait(2L, () => transport.PacketStat.SentPackets);
        GC.KeepAlive(delayedPacket);
        GC.KeepAlive(nextPacket);
    }

    [TestMethod]
    public void Passthrough_transport_that_hands_packets_on_disposes_what_it_does_not_hand_on()
    {
        var transport = new TestPassthroughPacketTransport { FailPort = 1 };

        // one no one takes
        var refusedMemory = BuildPacketMemory(1);
        var refusedPacket = PacketBuilder.Attach(refusedMemory);
        Assert.IsFalse(transport.SendPacketQueued(refusedPacket));
        Assert.IsTrue(refusedMemory.IsDisposed);

        // one handed on is no longer the transport's
        var sentMemory = BuildPacketMemory(2);
        var sentPacket = PacketBuilder.Attach(sentMemory);
        Assert.IsTrue(transport.SendPacketQueued(sentPacket));
        Assert.IsFalse(sentMemory.IsDisposed);

        // one sent to it once disposed
        transport.Dispose();
        var lateMemory = BuildPacketMemory(3);
        var latePacket = PacketBuilder.Attach(lateMemory);
        Assert.ThrowsExactly<ObjectDisposedException>(() => transport.SendPacketQueued(latePacket));
        Assert.IsTrue(lateMemory.IsDisposed);
        Assert.AreEqual(2, transport.PacketStat.DroppedPackets);
        GC.KeepAlive(refusedPacket);
        GC.KeepAlive(sentPacket);
        GC.KeepAlive(latePacket);
    }

    [TestMethod]
    public void Received_packet_the_transport_cannot_deliver_is_disposed()
    {
        // a transport that hands its packets on: only the rule for an undelivered packet disposes these
        var transport = new TestHeldPacketTransport(queueCapacity: 1, autoDisposePackets: false);

        // with no handler
        var unhandledMemory = BuildPacketMemory(1);
        var unhandledPacket = PacketBuilder.Attach(unhandledMemory);
        transport.Receive(unhandledPacket);
        Assert.IsTrue(unhandledMemory.IsDisposed);

        // given back by a handler that threw; one the handler takes is its own
        transport.PacketReceived += (_, ipPacket) => {
            if (ipPacket.ExtractUdp().SourcePort == 2)
                throw new InvalidOperationException("Test: the handler failed.");
        };
        var givenBackMemory = BuildPacketMemory(2);
        var givenBackPacket = PacketBuilder.Attach(givenBackMemory);
        transport.Receive(givenBackPacket);
        Assert.IsTrue(givenBackMemory.IsDisposed);
        var takenMemory = BuildPacketMemory(3);
        var takenPacket = PacketBuilder.Attach(takenMemory);
        transport.Receive(takenPacket);
        Assert.IsFalse(takenMemory.IsDisposed);

        // refused once the transport is disposed
        transport.Dispose();
        var refusedMemory = BuildPacketMemory(4);
        var refusedPacket = PacketBuilder.Attach(refusedMemory);
        transport.Receive(refusedPacket);
        Assert.IsTrue(refusedMemory.IsDisposed);
        GC.KeepAlive(unhandledPacket);
        GC.KeepAlive(givenBackPacket);
        GC.KeepAlive(takenPacket);
        GC.KeepAlive(refusedPacket);
    }

    [TestMethod]
    public async Task Failed_send_is_counted_and_disposed_and_the_queue_goes_on()
    {
        using var transport = new TestHeldPacketTransport(queueCapacity: 4) { FailSendPort = 1 };
        transport.Release();

        var failedMemory = BuildPacketMemory(1);
        var failedPacket = PacketBuilder.Attach(failedMemory);
        Assert.IsTrue(transport.SendPacketQueued(failedPacket));
        await AssertEqualsWait(1L, () => transport.PacketStat.DroppedPackets);
        Assert.IsTrue(failedMemory.IsDisposed);

        Assert.IsTrue(transport.SendPacketQueued(BuildPacket(2)));
        await AssertEqualsWait(1, () => transport.SentPorts.Count);
        Assert.AreEqual(2, transport.SentPorts[0]);
        GC.KeepAlive(failedPacket);
    }

    [TestMethod]
    public async Task Send_loop_whose_cleanup_fails_disposes_its_batch_and_the_transport()
    {
        using var transport = new TestHeldPacketTransport(queueCapacity: 4) {
            FailSendPort = 1,
            FailSendErrorLog = true
        };
        transport.Release();

        // the send fails, and so does logging it: the packet is disposed all the same, and the loop that cannot go
        // on leaves the transport disposed, so later sends fail loud
        var memory = BuildPacketMemory(1);
        var ipPacket = PacketBuilder.Attach(memory);
        Assert.IsTrue(transport.SendPacketQueued(ipPacket));
        await AssertEqualsWait(true, () => transport.IsTransportDisposed);
        Assert.IsTrue(memory.IsDisposed);
        Assert.ThrowsExactly<ObjectDisposedException>(() => transport.SendPacketQueued(BuildPacket(2)));
        GC.KeepAlive(ipPacket);
    }

    [TestMethod]
    public void Dispose_whose_hooks_throw_still_disposes()
    {
        var transport = new TestHeldPacketTransport(queueCapacity: 1) {
            FailPreDispose = true,
            FailDisposeManaged = true
        };

        // the failure surfaces, and the transport is disposed all the same, its unmanaged part too
        Assert.ThrowsExactly<InvalidOperationException>(transport.Dispose);
        Assert.IsTrue(transport.IsTransportDisposed);
        Assert.AreEqual(1, transport.DisposeUnmanagedCount);

        var memory = BuildPacketMemory(1);
        var ipPacket = PacketBuilder.Attach(memory);
        Assert.ThrowsExactly<ObjectDisposedException>(() => transport.SendPacketQueued(ipPacket));
        Assert.IsTrue(memory.IsDisposed);
        GC.KeepAlive(ipPacket);
    }
}
