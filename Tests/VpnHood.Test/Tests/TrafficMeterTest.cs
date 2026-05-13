using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Test.Dom;
using VpnHood.Test.Packets;

namespace VpnHood.Test.Tests;

[TestClass]
public class TrafficMeterTest : TestBase
{
    private class ServerUdpChannelTransmitterTest(UdpClient udpClient)
        : UdpChannelTransmitter(udpClient)
    {
        private readonly Dictionary<ulong, SessionUdpTransport> _transports = new();

        public SessionUdpTransport AddSession(ulong sessionId, byte[] sessionKey)
        {
            var transport = new SessionUdpTransport(this, sessionId, sessionKey, null, true);
            _transports[sessionId] = transport;
            return transport;
        }

        protected override SessionUdpTransport? SessionIdToUdpTransport(ulong sessionId)
            => _transports.GetValueOrDefault(sessionId);

        public override void Dispose()
        {
            foreach (var transport in _transports.Values)
                transport.Dispose();
            _transports.Clear();
            base.Dispose();
        }
    }

    private class ClientUdpChannelTransmitterTest : UdpChannelTransmitter
    {
        public SessionUdpTransport UdpTransport { get; }

        public ClientUdpChannelTransmitterTest(IPEndPoint remoteEndPoint, ulong sessionId, byte[] sessionKey)
            : base(new UdpClient(0, remoteEndPoint.AddressFamily))
        {
            UdpTransport = new SessionUdpTransport(this, sessionId, sessionKey, remoteEndPoint, false);
        }

        protected override SessionUdpTransport SessionIdToUdpTransport(ulong sessionId) => UdpTransport;

        public override void Dispose()
        {
            UdpTransport.Dispose();
            base.Dispose();
        }
    }

    private sealed class ServerUdpTunnelFixture : IDisposable
    {
        public Tunnel Tunnel { get; }
        public UdpChannel Channel { get; }
        private ServerUdpChannelTransmitterTest Transmitter { get; }
        public IPEndPoint LocalEndPoint => Transmitter.LocalEndPoint;

        public ServerUdpTunnelFixture(TestHelper testHelper, ulong sessionId, byte[] sessionKey)
        {
            Tunnel = new Tunnel(testHelper.CreateTunnelOptions());

            Transmitter = new ServerUdpChannelTransmitterTest(new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)));
            Channel = new UdpChannel(Transmitter.AddSession(sessionId, sessionKey), new UdpChannelOptions {
                AutoDisposePackets = true,
                Blocking = false,
                ChannelId = Guid.CreateVersion7().ToString(),
                Lifespan = null,
                TrafficMeter = Tunnel.TrafficMeter
            });
            Tunnel.AddChannel(Channel);
        }

        public void Dispose()
        {
            Channel.Dispose();
            Transmitter.Dispose();
            Tunnel.Dispose();
        }
    }

    private sealed class ClientUdpTunnelFixture : IDisposable
    {
        public Tunnel Tunnel { get; }
        private UdpChannel Channel { get; }
        private readonly ClientUdpChannelTransmitterTest _transmitter;

        public ClientUdpTunnelFixture(TestHelper testHelper, IPEndPoint serverEndPoint, ulong sessionId,
            byte[] sessionKey)
        {
            Tunnel = new Tunnel(testHelper.CreateTunnelOptions());

            _transmitter = new ClientUdpChannelTransmitterTest(serverEndPoint, sessionId, sessionKey);
            Channel = new UdpChannel(_transmitter.UdpTransport, new UdpChannelOptions {
                AutoDisposePackets = true,
                Blocking = false,
                ChannelId = Guid.CreateVersion7().ToString(),
                Lifespan = null,
                TrafficMeter = Tunnel.TrafficMeter
            });
            Tunnel.AddChannel(Channel);
        }

        public void Dispose()
        {
            Channel.Dispose();
            _transmitter.Dispose();
            Tunnel.Dispose();
        }
    }

    [TestMethod]
    public async Task Tracks_sent_and_received_via_Tunnel()
    {
        var sessionId = 1UL;
        var sessionKey = VhUtils.GenerateKey();

        using var server = new ServerUdpTunnelFixture(TestHelper, sessionId, sessionKey);
        // ReSharper disable once AccessToDisposedClosure
        server.Tunnel.PacketReceived += (_, ipPacket) => server.Channel.SendPacketQueued(ipPacket.Clone());

        using var client = new ClientUdpTunnelFixture(TestHelper, server.LocalEndPoint, sessionId, sessionKey);
        var receivedCount = 0;
        client.Tunnel.PacketReceived += (_, _) => receivedCount++;

        // send packets through the tunnel
        var packets = new[] {
            PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true)),
            PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true)),
            PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true))
        };
        var sentBytes = packets.Sum(p => p.PacketLength);

        foreach (var packet in packets)
            client.Tunnel.SendPacketQueued(packet);

        await AssertEqualsWait(packets.Length, () => receivedCount);

        Assert.IsGreaterThanOrEqualTo(sentBytes, client.Tunnel.TrafficMeter.Traffic.Sent,
            "TrafficMeter should track sent bytes.");
        Assert.IsGreaterThan(0, client.Tunnel.TrafficMeter.Traffic.Received,
            "TrafficMeter should track received (echoed) bytes.");
        Assert.IsGreaterThan(DateTime.MinValue, client.Tunnel.TrafficMeter.LastActivityTime,
            "LastActivityTime should be updated.");
    }

    [TestMethod]
    public async Task Calculates_speed_via_Tunnel()
    {
        var sessionId = 1UL;
        var sessionKey = VhUtils.GenerateKey();

        using var server = new ServerUdpTunnelFixture(TestHelper, sessionId, sessionKey);
        // ReSharper disable once AccessToDisposedClosure
        server.Tunnel.PacketReceived += (_, ipPacket) => server.Channel.SendPacketQueued(ipPacket.Clone());

        using var client = new ClientUdpTunnelFixture(TestHelper, server.LocalEndPoint, sessionId, sessionKey);
        var receivedCount = 0;
        client.Tunnel.PacketReceived += (_, _) => receivedCount++;

        // send packets and wait for round-trip
        var packets = Enumerable.Range(0, 10)
            .Select(_ => PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true)))
            .ToArray();

        foreach (var packet in packets)
            client.Tunnel.SendPacketQueued(packet);

        await AssertEqualsWait(packets.Length, () => receivedCount);

        // wait for the speed meter interval to pass (> 1 second)
        await Task.Delay(TimeSpan.FromSeconds(1.5), TestCt);

        // send one more to trigger a speed update
        client.Tunnel.SendPacketQueued(PacketBuilder.Parse(NetPacketBuilder.RandomPacket(true)));
        await AssertEqualsWait(packets.Length + 1, () => receivedCount);

        var speed = client.Tunnel.TrafficMeter.Speed;
        Assert.IsGreaterThan(0, speed.Sent, "Send speed should be greater than zero.");
        Assert.IsGreaterThan(0, speed.Received, "Receive speed should be greater than zero.");
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

    [TestMethod]
    public async Task Max_upload_via_StreamPacketChannel()
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
    public async Task Max_download_via_StreamPacketChannel()
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

    [TestMethod]
    public async Task Throttles_max_speed_via_ProxyChannel()
    {
        // create two loopback TCP connection pairs to act as tunnel and host sides
        var endPoint1 = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback);
        var listener1 = new TcpListener(endPoint1);
        listener1.Start();
        var endPoint2 = VhUtils.GetFreeTcpEndPoint(IPAddress.Loopback);
        var listener2 = new TcpListener(endPoint2);
        listener2.Start();

        using var tunnelClient = new TcpClient();
        using var hostClient = new TcpClient();
        await Task.WhenAll(
            tunnelClient.ConnectAsync(endPoint1, TestCt).AsTask(),
            hostClient.ConnectAsync(endPoint2, TestCt).AsTask());
        using var tunnelServer = await listener1.AcceptTcpClientAsync(TestCt);
        using var hostServer = await listener2.AcceptTcpClientAsync(TestCt);
        listener1.Stop();
        listener2.Stop();

        var trafficMeter = new TrafficMeter {
            MaxSpeed = new Traffic(sent: 1, received: 0) // 1 byte/sec
        };

        // ProxyChannel bridges tunnelServer <-> hostServer
        await using var tunnelConn = new TcpConnection(tunnelServer, connectionName: "tunnel", isServer: true);
        await using var hostConn = new TcpConnection(hostServer, connectionName: "host", isServer: false);
        using var proxyChannel = new ProxyChannel(
            Guid.NewGuid().ToString(),
            tunnelConn,
            hostConn,
            TunnelDefaults.ClientStreamProxyBufferSize,
            trafficMeter);
        proxyChannel.Start();

        // report bytes to exceed the limit, then verify ThrottleAsync delays accordingly
        trafficMeter.OnSent(500);

        var stopwatch = Stopwatch.StartNew();
        await trafficMeter.ThrottleAsync(TestCt);
        stopwatch.Stop();

        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(1), stopwatch.Elapsed,
            $"ProxyChannel TrafficMeter should throttle. Elapsed: {stopwatch.Elapsed.TotalSeconds:F2}s");
    }
}
