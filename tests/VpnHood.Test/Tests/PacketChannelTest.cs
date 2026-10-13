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
    public void Close_received_once_the_channel_is_disposed_is_disposed_with_no_reply()
    {
        var channel = new TestPacketChannel();
        channel.Dispose();

        // a read loop may still hand on a packet it read before the stop
        var memory = ToMemory(PacketMessageHandler.CreateMessage(new ClosePacketMessage()));
        var close = PacketBuilder.Attach(memory);
        channel.Receive(close);
        Assert.IsTrue(memory.IsDisposed);
        Assert.AreEqual(0, channel.PacketStat.DroppedPackets);
        GC.KeepAlive(close);
    }
}
