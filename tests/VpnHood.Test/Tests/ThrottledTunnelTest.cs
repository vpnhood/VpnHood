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
    private static TunnelOptions CreateTunnelOptions()
    {
        return new TunnelOptions {
            MaxPacketChannelCount = 1,
            Mtu = TransportDefaults.MtuClient
        };
    }

    private static TrafficMeter CreateTrafficMeter(long maxSpeed)
    {
        return new TrafficMeter {
            MaxSpeed = new Traffic(sent: maxSpeed, received: 0),
            MaxSpeedBurst = new Traffic(sent: 0, received: 0)
        };
    }

    [TestMethod]
    public void Throttled_tunnel_needs_a_send_limit()
    {
        // it would have nothing to pace its packets to
        using var trafficMeter = CreateTrafficMeter(maxSpeed: 0);
        Assert.ThrowsExactly<ArgumentException>(() => new ThrottledTunnel(CreateTunnelOptions(), trafficMeter));
    }

    [TestMethod]
    public async Task Dispose_ends_the_wait_on_the_limit_and_drops_the_queued_packets()
    {
        // at 1000 B/s with no burst, a turn is the smallest, 2 KB: the queue's second packet ends it, and the queue
        // waits more than two seconds
        using var trafficMeter = CreateTrafficMeter(maxSpeed: 1000);
        using var tunnel = new ThrottledTunnel(CreateTunnelOptions(), trafficMeter);
        var channel = new TestPacketChannel();
        tunnel.AddChannel(channel, disposeIfFailed: true);
        for (var i = 0; i < 5; i++)
            tunnel.SendPacketQueued(PacketBuilder.BuildUdp(IPAddress.Parse("10.0.0.2"), IPAddress.Parse("10.0.0.3"),
                1000, 2000, new byte[1200]));
        await AssertEqualsWait(2, () => channel.SentPackets);

        // the three left wait in the queue, which the tunnel counts as its own
        Assert.AreEqual(3, tunnel.QueueLength);
        Assert.IsTrue(tunnel.IsSending);

        // the disposal does not wait for the limit, and the packets still queued are dropped
        var stopwatch = Stopwatch.StartNew();
        tunnel.Dispose();
        Assert.IsLessThan(TimeSpan.FromMilliseconds(500), stopwatch.Elapsed);
        Assert.AreEqual(3, tunnel.QueueDroppedPackets);
        Assert.AreEqual(2, channel.SentPackets);
    }
}
