using System.Net;
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
}
