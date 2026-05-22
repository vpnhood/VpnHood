using System.Diagnostics;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Tunneling;
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
        
        await Task.Delay(1000); //todo: remove

        Assert.IsGreaterThanOrEqualTo(100_000, clientServerDom.Client.Session!.Status.SessionTraffic.Sent,
            "TrafficMeter should track sent bytes.");
        Assert.IsGreaterThan(60_000, clientServerDom.Client.Session.Status.SessionTraffic.Received,
            "TrafficMeter should track received bytes.");
    }


    [TestMethod]
    public async Task Throttles_max_speed()
    {
        using var trafficMeter = new TrafficMeter();
        trafficMeter.MaxSpeed = new Traffic(sent: 100, received: 0); // 100 bytes/sec send limit

        // report 1000 bytes sent — 10x over the limit
        trafficMeter.OnSent(1000);

        var stopwatch = Stopwatch.StartNew();
        await trafficMeter.ThrottleAsync(TestCt);
        stopwatch.Stop();

        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(1), stopwatch.Elapsed,
            $"ThrottleAsync should have delayed at least 1 second. Elapsed: {stopwatch.Elapsed.TotalSeconds:F2}s");
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

        // Upload is throttled at 1 Mbps; sending 1 MB should take well over 500 ms
        var uploadStopwatch = Stopwatch.StartNew();
        await TestHelper.Test_QuicUpload(100_000, cancellationToken: TestCt);
        uploadStopwatch.Stop();
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500), uploadStopwatch.Elapsed,
            $"Upload should be throttled (>500 ms). Elapsed: {uploadStopwatch.Elapsed.TotalMilliseconds:F0} ms");

        // Download has no throttle; receiving 1 MB locally should complete in under 100 ms
        var downloadStopwatch = Stopwatch.StartNew();
        await TestHelper.Test_QuicDownload(100_000, cancellationToken: TestCt);
        downloadStopwatch.Stop();
        Assert.IsLessThan(TimeSpan.FromMilliseconds(100), downloadStopwatch.Elapsed,
            $"Download should not be throttled (<100 ms). Elapsed: {downloadStopwatch.Elapsed.TotalMilliseconds:F0} ms");
    }

    // We use QUIC to make sure packet channel is used
    [TestMethod]
    [DataRow(ChannelProtocol.Tcp)]
    [DataRow(ChannelProtocol.Udp)]
    public async Task PacketChannel_max_download(ChannelProtocol channelProtocol)
    {
        // Note: in tests the IsProxyMode is always true

        var clientOption = TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Tcp);
        await using var clientServerDom = await ClientServerDom.Create(TestHelper,
            clientOption, maxSpeedMbps: new Traffic(sent: 0, received: 1));

        // Upload has no throttle; sending 1 MB locally should complete in under 100 ms
        var uploadStopwatch = Stopwatch.StartNew();
        await TestHelper.Test_QuicUpload(100_000, cancellationToken: TestCt);
        uploadStopwatch.Stop();
        Assert.IsLessThan(TimeSpan.FromMilliseconds(100), uploadStopwatch.Elapsed,
            $"Upload should not be throttled (<100 ms). Elapsed: {uploadStopwatch.Elapsed.TotalMilliseconds:F0} ms");

        // Download is throttled at 1 Mbps; receiving 1 MB should take well over 500 ms
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

        // Upload is throttled at 1 Mbps; sending 1 MB should take well over 500 ms
        var uploadStopwatch = Stopwatch.StartNew();
        await TestHelper.Test_TcpUpload(100_000, cancellationToken: TestCt);
        uploadStopwatch.Stop();
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500), uploadStopwatch.Elapsed,
            $"Upload should be throttled (>500 ms). Elapsed: {uploadStopwatch.Elapsed.TotalMilliseconds:F0} ms");

        // Download has no throttle; receiving 1 MB locally should complete in under 100 ms
        var downloadStopwatch = Stopwatch.StartNew();
        await TestHelper.Test_TcpDownload(100_000, cancellationToken: TestCt);
        downloadStopwatch.Stop();
        Assert.IsLessThan(TimeSpan.FromMilliseconds(100), downloadStopwatch.Elapsed,
            $"Download should not be throttled (<100 ms). Elapsed: {downloadStopwatch.Elapsed.TotalMilliseconds:F0} ms");
    }

    [TestMethod]
    public async Task ProxyChannel_max_download()
    {
        // Note: in tests the IsProxyMode is always true

        var clientOption = TestHelper.CreateClientOptions(channelProtocol: ChannelProtocol.Tcp);
        await using var clientServerDom = await ClientServerDom.Create(TestHelper,
            clientOption, maxSpeedMbps: new Traffic(sent: 0, received: 1));

        // Upload has no throttle; sending 1 MB locally should complete in under 100 ms
        var uploadStopwatch = Stopwatch.StartNew();
        await TestHelper.Test_TcpUpload(100_000, cancellationToken: TestCt);
        uploadStopwatch.Stop();
        Assert.IsLessThan(TimeSpan.FromMilliseconds(100), uploadStopwatch.Elapsed,
            $"Upload should not be throttled (<100 ms). Elapsed: {uploadStopwatch.Elapsed.TotalMilliseconds:F0} ms");

        // Download is throttled at 1 Mbps; receiving 1 MB should take well over 500 ms
        var downloadStopwatch = Stopwatch.StartNew();
        await TestHelper.Test_TcpDownload(100_000, cancellationToken: TestCt);
        downloadStopwatch.Stop();
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500), downloadStopwatch.Elapsed,
            $"Download should be throttled (>500 ms). Elapsed: {downloadStopwatch.Elapsed.TotalMilliseconds:F0} ms");
    }
}
