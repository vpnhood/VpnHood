using System.Diagnostics;
using System.Net;
using VpnHood.Core.Common.Configuration;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Tunneling;
using VpnHood.Net.Packets;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class ThrottledTunnelTest : TestBase
{
    private static TunnelOptions CreateTunnelOptions(long maxSpeed)
    {
        return new TunnelOptions {
            MaxPacketChannelCount = 1,
            Mtu = TransportDefaults.MtuClient,
            MaxSpeed = new Traffic(sent: maxSpeed, received: 0),
            MaxSpeedBurst = new Traffic(sent: 0, received: 0)
        };
    }

    [TestMethod]
    public void Send_limit_and_throttling_go_together()
    {
        // a plain tunnel would pace its proxy streams but not its packets; a throttled one would have nothing
        // to pace them to
        Assert.ThrowsExactly<ArgumentException>(() => new Tunnel(CreateTunnelOptions(maxSpeed: 1000)));
        Assert.ThrowsExactly<ArgumentException>(() => new ThrottledTunnel(CreateTunnelOptions(maxSpeed: 0)));
    }

    [TestMethod]
    public async Task Dispose_ends_the_wait_on_the_limit_and_drops_the_queued_packets()
    {
        // at 1000 B/s with no burst, the first packet makes the queue wait more than a second
        using var tunnel = new ThrottledTunnel(CreateTunnelOptions(maxSpeed: 1000));
        var channel = new TestPacketChannel();
        tunnel.AddChannel(channel, disposeIfFailed: true);
        for (var i = 0; i < 5; i++)
            tunnel.SendPacketQueued(PacketBuilder.BuildUdp(IPAddress.Parse("10.0.0.2"), IPAddress.Parse("10.0.0.3"),
                1000, 2000, new byte[1200]));
        await AssertEqualsWait(1, () => channel.SentPackets);

        // the disposal does not wait for the limit, and the packets still queued are dropped
        var stopwatch = Stopwatch.StartNew();
        tunnel.Dispose();
        Assert.IsLessThan(TimeSpan.FromMilliseconds(500), stopwatch.Elapsed);
        Assert.AreEqual(4, tunnel.QueueDroppedPackets);
        Assert.AreEqual(1, channel.SentPackets);
    }
}
