using System.Diagnostics;
using System.Net;
using VpnHood.Core.Common.Configuration;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.DatagramMessaging;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class PacketChannelTest : TestBase
{
    // the packet over a memory that records its disposal
    private static TestMemoryOwner ToMemory(IpPacket ipPacket)
    {
        using (ipPacket)
            return new TestMemoryOwner(ipPacket.Buffer.ToArray());
    }

    private static IpPacket BuildPacket(int sourcePort)
    {
        return PacketBuilder.BuildUdp(IPAddress.Parse("10.0.0.2"), IPAddress.Parse("10.0.0.3"), sourcePort, 53,
            new byte[16]);
    }

    private static IpPacket BuildClose()
    {
        return PacketMessageHandler.CreateMessage(new ClosePacketMessage());
    }

    [TestMethod]
    public void Message_the_channel_cannot_read_is_dropped_and_the_channel_goes_on()
    {
        using var channel = new TestPacketChannel();
        channel.Start();
        var receivedPorts = new List<int>();
        channel.PacketReceived += (_, ipPacket) => {
            receivedPorts.Add(ipPacket.ExtractUdp().SourcePort);
            ipPacket.Dispose();
        };

        // a message of a version this one does not know, as a newer peer's may be
        var memory = ToMemory(PacketBuilder.BuildUdp(IPAddress.Any, IPAddress.Any, 0, 0, [2, 0]));
        var message = PacketBuilder.Attach(memory);
        channel.Receive(message);
        Assert.IsTrue(memory.IsDisposed);

        // the channel goes on
        channel.Receive(PacketBuilder.BuildUdp(IPAddress.Parse("10.0.0.2"), IPAddress.Parse("10.0.0.3"), 1, 53,
            new byte[16]));
        Assert.AreEqual(PacketChannelState.Connected, channel.State);
        CollectionAssert.AreEqual(new[] { 1 }, receivedPorts);
        GC.KeepAlive(message);
    }

    [TestMethod]
    public async Task Channel_whose_lifespan_ends_is_disconnecting_and_sends_its_close()
    {
        using var channel = new TestPacketChannel(lifespan: TimeSpan.Zero);
        channel.Start();

        // the lifetime job finds the lifespan over: its close goes out, and the tunnel picks the channel no more
        await AssertEqualsWait(1, () => channel.SentPackets);
        Assert.AreEqual(PacketChannelState.Disconnecting, channel.State);
    }

    [TestMethod]
    public async Task Close_received_makes_the_channel_disconnecting_with_no_answer()
    {
        using var channel = new TestPacketChannel();
        channel.Start();

        var memory = ToMemory(PacketMessageHandler.CreateMessage(new ClosePacketMessage()));
        var close = PacketBuilder.Attach(memory);
        channel.Receive(close);
        Assert.IsTrue(memory.IsDisposed);
        Assert.AreEqual(PacketChannelState.Disconnecting, channel.State);

        // a packet sent after it goes alone: no answer went before it
        channel.SendPacketQueued(PacketBuilder.BuildUdp(IPAddress.Parse("10.0.0.2"), IPAddress.Parse("10.0.0.3"), 1, 53,
            new byte[16]));
        await AssertEqualsWait(false, () => channel.IsSending);
        Assert.AreEqual(1, channel.SentPackets);
        GC.KeepAlive(close);
    }

    [TestMethod]
    public async Task Open_channel_outlives_its_lifetime_checks()
    {
        using var channel = new TestPacketChannel();
        channel.Start();

        // the lifetime job runs at once, then every second
        await Task.Delay(1500, TestCt);
        Assert.AreEqual(PacketChannelState.Connected, channel.State);
    }

    [TestMethod]
    public async Task Channel_closed_by_its_owner_sends_its_close_and_still_delivers()
    {
        using var channel = new TestPacketChannel();
        channel.Start();
        var receivedPorts = new List<int>();
        channel.PacketReceived += (_, ipPacket) => {
            receivedPorts.Add(ipPacket.ExtractUdp().SourcePort);
            ipPacket.Dispose();
        };

        // its close goes out, and it is Disconnecting, not gone
        channel.Close();
        await AssertEqualsWait(1, () => channel.SentPackets);
        Assert.AreEqual(PacketChannelState.Disconnecting, channel.State);

        // what arrives in its grace is still delivered
        channel.Receive(BuildPacket(1));
        CollectionAssert.AreEqual(new[] { 1 }, receivedPorts);
    }

    [TestMethod]
    public async Task Channel_closed_before_its_start_stays_disconnecting()
    {
        using var channel = new TestPacketChannel();
        channel.Close();
        channel.Start();

        await AssertEqualsWait(1, () => channel.SentPackets);
        Assert.AreEqual(PacketChannelState.Disconnecting, channel.State);
    }

    [TestMethod]
    public async Task Close_after_the_first_sends_nothing_more()
    {
        using var closedTwice = new TestPacketChannel();
        closedTwice.Start();
        closedTwice.Close();
        closedTwice.Close();

        using var closedThenReceived = new TestPacketChannel();
        closedThenReceived.Start();
        closedThenReceived.Close();
        closedThenReceived.Receive(BuildClose());

        // the peer's close first: no answer
        using var receivedThenClosed = new TestPacketChannel();
        receivedThenClosed.Start();
        receivedThenClosed.Receive(BuildClose());
        receivedThenClosed.Close();

        // closed before its lifespan ends, which a later lifetime check meets
        using var closedThenLifespan = new TestPacketChannel(lifespan: TimeSpan.FromMilliseconds(500));
        closedThenLifespan.Start();
        closedThenLifespan.Close();

        // past the lifespan and a lifetime check after it, every queued send done
        await Task.Delay(2500, TestCt);
        foreach (var channel in new[] { closedTwice, closedThenReceived, receivedThenClosed, closedThenLifespan })
            await AssertEqualsWait(false, () => channel.IsSending);

        Assert.AreEqual(1, closedTwice.SentPackets);
        Assert.AreEqual(1, closedThenReceived.SentPackets);
        Assert.AreEqual(0, receivedThenClosed.SentPackets);
        Assert.AreEqual(1, closedThenLifespan.SentPackets);
    }

    [TestMethod]
    public async Task Concurrent_closes_send_one_close_at_most()
    {
        using var closed = new TestPacketChannel();
        closed.Start();
        using var raced = new TestPacketChannel();
        raced.Start();

        // closes released together: owners' closes racing each other, one of which sends the close; and owners'
        // racing the peer's, which may win and send nothing
        using var startGate = new ManualResetEventSlim();
        var closers = Enumerable.Range(0, 8).Select(i => Task.Run(() => {
            startGate.Wait(TestCt);
            closed.Close();
            if (i % 2 == 0)
                raced.Close();
            else
                raced.Receive(BuildClose());
        }, TestCt)).ToArray();
        startGate.Set();
        await Task.WhenAll(closers);

        // every queued send done
        await AssertEqualsWait(false, () => closed.IsSending);
        await AssertEqualsWait(false, () => raced.IsSending);
        Assert.AreEqual(1, closed.SentPackets);
        Assert.IsLessThanOrEqualTo(1, raced.SentPackets);
        Assert.AreEqual(PacketChannelState.Disconnecting, raced.State);
    }

    [TestMethod]
    public void Close_of_a_disposed_channel_does_nothing()
    {
        var channel = new TestPacketChannel();
        channel.Start();
        channel.Dispose();

        channel.Close();
        Assert.AreEqual(PacketChannelState.Disposed, channel.State);
        Assert.AreEqual(0, channel.SentPackets);
    }

    [TestMethod]
    [DoNotParallelize] // shortens the close grace, which is process-global
    public async Task Later_closes_do_not_extend_the_grace()
    {
        var gracefulTimeout = TransportDefaults.TcpGracefulTimeout;
        TransportDefaults.TcpGracefulTimeout = TimeSpan.FromSeconds(1);
        try {
            using var channel = new TestPacketChannel();
            channel.Start();
            channel.Close();

            // closes keep coming, more often than the grace lasts: had one restarted it, the channel would stay
            var stopwatch = Stopwatch.StartNew();
            while (channel.State != PacketChannelState.Disposed && stopwatch.Elapsed < TimeSpan.FromSeconds(10)) {
                channel.Close();
                channel.Receive(BuildClose());
                await Task.Delay(200, TestCt);
            }

            Assert.AreEqual(PacketChannelState.Disposed, channel.State);
        }
        finally {
            TransportDefaults.TcpGracefulTimeout = gracefulTimeout;
        }
    }
}
