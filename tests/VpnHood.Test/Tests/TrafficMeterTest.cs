using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Common.Configuration;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Test.Dom;

namespace VpnHood.Test.Tests;

[TestClass]
public class TrafficMeterTest : TestBase
{
    [TestMethod]
    public async Task Tunnel_tracks_sent_and_received()
    {
        var clientOption = TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Tcp);
        await using var clientServerDom = await ClientServerDom.Create(TestHelper,
            clientOption);
        await Task.WhenAll(
            TestHelper.Test_TcpUpload(100_000, cancellationToken: TestCt),
            TestHelper.Test_TcpDownload(60_0000, cancellationToken: TestCt));
        
        Assert.IsGreaterThanOrEqualTo(100_000, clientServerDom.Client.Session!.Status.SessionTraffic.Sent,
            "TrafficMeter should track sent bytes.");
        Assert.IsGreaterThan(60_000, clientServerDom.Client.Session.Status.SessionTraffic.Received,
            "TrafficMeter should track received bytes.");
    }


    [TestMethod]
    public async Task Throttles_max_speed()
    {
        using var trafficMeter = new TrafficMeter {
            MaxSpeed = new Traffic(sent: 1000, received: 0), // 1000 bytes/sec send limit
            MaxSpeedBurst = new Traffic()
        };

        // 1000 bytes with no burst: a second at the limit
        var stopwatch = Stopwatch.StartNew();
        await trafficMeter.ThrottleSendAsync(1000, TestCt);
        stopwatch.Stop();

        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900), stopwatch.Elapsed,
            $"ThrottleSendAsync should have delayed about 1 second. Elapsed: {stopwatch.Elapsed.TotalSeconds:F2}s");
    }

    [TestMethod]
    public async Task A_pause_saves_at_most_the_burst()
    {
        using var trafficMeter = new TrafficMeter {
            MaxSpeed = new Traffic(sent: 100_000, received: 0),
            MaxSpeedBurst = new Traffic(sent: 50_000, received: 0)
        };

        // past the burst, then a pause that would save 150 KB had nothing capped it
        await trafficMeter.ThrottleSendAsync(100_000, TestCt);
        await Task.Delay(TimeSpan.FromSeconds(1.5), TestCt);

        // the burst passes at once, and the next bytes wait for the limit
        var stopwatch = Stopwatch.StartNew();
        await trafficMeter.ThrottleSendAsync(50_000, TestCt);
        Assert.IsLessThan(TimeSpan.FromMilliseconds(100), stopwatch.Elapsed, $"Burst: {stopwatch.Elapsed}");

        stopwatch.Restart();
        await trafficMeter.ThrottleSendAsync(50_000, TestCt);
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(400), stopwatch.Elapsed, $"Past it: {stopwatch.Elapsed}");
    }

    [TestMethod]
    public void Receive_police_drops_past_the_burst()
    {
        // 10 KB/s with a 3 KB burst; the police also lets through 100 ms of the limit as debt, 1 KB
        using var trafficMeter = new TrafficMeter {
            MaxSpeed = new Traffic(sent: 0, received: 10_000),
            MaxSpeedBurst = new Traffic(sent: 0, received: 3_000)
        };

        var accepted = 0;
        for (var i = 0; i < 10; i++)
            if (!trafficMeter.ShouldThrottleReceive(1000))
                accepted++;

        Assert.IsTrue(accepted is >= 4 and <= 5, $"Accepted: {accepted}");
    }

    [TestMethod]
    public async Task Throttled_tunnel_paces_its_packets_without_holding_the_sender()
    {
        // a tunnel throttled to 1 Mbps, with one packet channel over loopback
        var tcpEndPoint = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback);
        var tcpListener = new TcpListener(tcpEndPoint);
        tcpListener.Start();
        var listenerTask = tcpListener.AcceptTcpClientAsync(TestCt).AsTask();
        using var tcpClient = new TcpClient();
        await Task.WhenAll(listenerTask, tcpClient.ConnectAsync(tcpEndPoint, TestCt).AsTask());
        using var serverTcpClient = await listenerTask;
        tcpListener.Stop();

        await using var serverConnection = new TcpStreamConnection(serverTcpClient, connectionName: "tunnel", isServer: true);
        using var serverChannel = new StreamPacketChannel(new StreamPacketChannelOptions {
            RequestTime = DateTime.UtcNow,
            StreamConnection = serverConnection,
            Blocking = false,
            AutoDisposePackets = true,
            Lifespan = null,
            ChannelId = Guid.NewGuid().ToString(),
            TrafficMeter = new TrafficMeter()
        });

        const long maxSpeed = 125_000;
        using var tunnel = new Tunnel(new TunnelOptions {
            MaxPacketChannelCount = 1,
            PacketQueueCapacity = TransportDefaults.TunnelPacketQueueCapacity,
            AutoDisposePackets = true,
            Mtu = TransportDefaults.MtuClient,
            MaxSpeed = new Traffic(sent: maxSpeed, received: 0),
            MaxSpeedBurst = new Traffic(sent: 16 * 1024, received: 0)
        });
        await using var clientConnection = new TcpStreamConnection(tcpClient, connectionName: "tunnel", isServer: false);
        tunnel.AddChannel(new StreamPacketChannel(new StreamPacketChannelOptions {
            RequestTime = DateTime.UtcNow,
            StreamConnection = clientConnection,
            Blocking = false,
            AutoDisposePackets = true,
            Lifespan = null,
            ChannelId = Guid.NewGuid().ToString(),
            TrafficMeter = tunnel.TrafficMeter
        }), disposeIfFailed: true);

        long receivedBytes = 0;
        serverChannel.PacketReceived += (_, ipPacket) => {
            Interlocked.Add(ref receivedBytes, ipPacket.PacketLength);
            ipPacket.Dispose();
        };
        serverChannel.Start();

        // an upload at several times the limit: no send waits, the limit holds the speed, and the
        // tunnel's queue drops the excess
        var longestSend = TimeSpan.Zero;
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; stopwatch.Elapsed < TimeSpan.FromSeconds(2); i++) {
            var ipPacket = PacketBuilder.BuildUdp(IPAddress.Parse("10.0.0.2"), IPAddress.Parse("10.0.0.3"),
                1000, 2000, new byte[1200]);
            var sendStopwatch = Stopwatch.StartNew();
            tunnel.SendPacketQueued(ipPacket);
            if (sendStopwatch.Elapsed > longestSend)
                longestSend = sendStopwatch.Elapsed;

            if (i % 10 == 0)
                await Task.Delay(1, TestCt);
        }

        var speed = Interlocked.Read(ref receivedBytes) / stopwatch.Elapsed.TotalSeconds;
        Assert.IsLessThan(TimeSpan.FromMilliseconds(100), longestSend, $"Longest send: {longestSend}");
        Assert.IsTrue(speed is > maxSpeed * 0.5 and < maxSpeed * 1.5, $"Speed: {speed:F0} B/s");
        Assert.IsGreaterThan(0, tunnel.PacketStat.DroppedPackets, "The tunnel's queue should drop the excess.");
    }

    // We use QUIC to make sure packet channel is used
    [TestMethod]
    [DataRow(ChannelProtocol.Tcp)]
    [DataRow(ChannelProtocol.Udp)]
    public async Task PacketChannel_max_upload(ChannelProtocol channelProtocol)
    {
        var clientOption = TestHelper.CreateClientOptions(channelProtocol: channelProtocol);
        await using var clientServerDom = await ClientServerDom.Create(TestHelper,
            clientOption, maxSpeedMbps: new Traffic(sent: 1, received: 0));

        // Upload is throttled at 1 Mbps; sending 100 KB should take well over 500 ms
        var uploadStopwatch = Stopwatch.StartNew();
        await TestHelper.Test_QuicUpload(100_000, cancellationToken: TestCt);
        uploadStopwatch.Stop();
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500), uploadStopwatch.Elapsed,
            $"Upload should be throttled (>500 ms). Elapsed: {uploadStopwatch.Elapsed.TotalMilliseconds:F0} ms");

        // Download has no throttle; receiving 100 KB locally should complete in under 300 ms
        var downloadStopwatch = Stopwatch.StartNew();
        await TestHelper.Test_QuicDownload(100_000, cancellationToken: TestCt);
        downloadStopwatch.Stop();
        Assert.IsLessThan(TimeSpan.FromMilliseconds(300), downloadStopwatch.Elapsed,
            $"Download should not be throttled (<300 ms). Elapsed: {downloadStopwatch.Elapsed.TotalMilliseconds:F0} ms");
    }

    // We use QUIC to make sure packet channel is used
    [TestMethod]
    [DataRow(ChannelProtocol.Tcp)]
    [DataRow(ChannelProtocol.Udp)]
    public async Task PacketChannel_max_download(ChannelProtocol channelProtocol)
    {
        // Note: in tests the IsProxyMode is always true

        var clientOption = TestHelper.CreateClientOptions(channelProtocol: channelProtocol);
        await using var clientServerDom = await ClientServerDom.Create(TestHelper,
            clientOption, maxSpeedMbps: new Traffic(sent: 0, received: 1));

        // Upload has no throttle; sending 100 KB locally should complete in under 300 ms
        var uploadStopwatch = Stopwatch.StartNew();
        await TestHelper.Test_QuicUpload(100_000, cancellationToken: TestCt);
        uploadStopwatch.Stop();
        Assert.IsLessThan(TimeSpan.FromMilliseconds(300), uploadStopwatch.Elapsed,
            $"Upload should not be throttled (<300 ms). Elapsed: {uploadStopwatch.Elapsed.TotalMilliseconds:F0} ms");

        // Download is throttled at 1 Mbps; receiving 100 KB should take well over 500 ms
        var downloadStopwatch = Stopwatch.StartNew();
        await TestHelper.Test_QuicDownload(100_000, cancellationToken: TestCt);
        downloadStopwatch.Stop();
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500), downloadStopwatch.Elapsed,
            $"Download should be throttled (>500 ms). Elapsed: {downloadStopwatch.Elapsed.TotalMilliseconds:F0} ms");
    }


    [TestMethod]
    public async Task ProxyChannel_max_upload()
    {
        // Note: in tests the IsProxyMode is always true

        var clientOption = TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Tcp);
        await using var clientServerDom = await ClientServerDom.Create(TestHelper,
            clientOption, maxSpeedMbps: new Traffic(sent: 1, received: 0));

        // Upload is throttled at 1 Mbps; sending 100 KB should take well over 500 ms
        var uploadStopwatch = Stopwatch.StartNew();
        await TestHelper.Test_TcpUpload(100_000, cancellationToken: TestCt);
        uploadStopwatch.Stop();
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500), uploadStopwatch.Elapsed,
            $"Upload should be throttled (>500 ms). Elapsed: {uploadStopwatch.Elapsed.TotalMilliseconds:F0} ms");

        // Download has no throttle; receiving 100 KB locally should complete in under 300 ms
        var downloadStopwatch = Stopwatch.StartNew();
        await TestHelper.Test_TcpDownload(100_000, cancellationToken: TestCt);
        downloadStopwatch.Stop();
        Assert.IsLessThan(TimeSpan.FromMilliseconds(300), downloadStopwatch.Elapsed,
            $"Download should not be throttled (<300 ms). Elapsed: {downloadStopwatch.Elapsed.TotalMilliseconds:F0} ms");
    }

    [TestMethod]
    public async Task ProxyChannel_max_download()
    {
        // Note: in tests the IsProxyMode is always true

        var clientOption = TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Tcp);
        await using var clientServerDom = await ClientServerDom.Create(TestHelper,
            clientOption, maxSpeedMbps: new Traffic(sent: 0, received: 1));

        // Upload has no throttle; sending 100 KB locally should complete in under 300 ms
        var uploadStopwatch = Stopwatch.StartNew();
        await TestHelper.Test_TcpUpload(100_000, cancellationToken: TestCt);
        uploadStopwatch.Stop();
        Assert.IsLessThan(TimeSpan.FromMilliseconds(300), uploadStopwatch.Elapsed,
            $"Upload should not be throttled (<300 ms). Elapsed: {uploadStopwatch.Elapsed.TotalMilliseconds:F0} ms");

        // Download is throttled at 1 Mbps; receiving 100 KB should take well over 500 ms
        var downloadStopwatch = Stopwatch.StartNew();
        await TestHelper.Test_TcpDownload(100_000, cancellationToken: TestCt);
        downloadStopwatch.Stop();
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500), downloadStopwatch.Elapsed,
            $"Download should be throttled (>500 ms). Elapsed: {downloadStopwatch.Elapsed.TotalMilliseconds:F0} ms");
    }
}
