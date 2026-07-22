using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Proxies;

namespace VpnHood.Test.Tests;

[TestClass]
public class UdpProxyTest : TestBase
{
    private class MyPacketProxyCallbacks : IPacketProxyCallbacks
    {
        // keep all packets, not just the last one; proxy workers report their echoes in any
        // order, and a single "last packet" slot loses the awaited echo when another packet
        // arrives after it
        private readonly ConcurrentQueue<IpPacket> _receivedPackets = new();

        public void PacketReceived(object? sender, IpPacket ipPacket)
        {
            _receivedPackets.Enqueue(ipPacket);
        }

        public Task WaitForUdpPacket(IpPacket ipPacket, TimeSpan? timeout = null)
        {
            // clone the request because the pool owns the queued packet; matching the payload
            // makes each wait find its own echo even when an older packet with the same
            // address signature is already queued
            var request = ipPacket.Clone();
            var requestUdp = request.ExtractUdp();

            return WaitForUdpPacket(p =>
                    p.DestinationAddress.Equals(request.SourceAddress) &&
                    p.SourceAddress.Equals(request.DestinationAddress) &&
                    p.ExtractUdp().DestinationPort == requestUdp.SourcePort &&
                    p.ExtractUdp().SourcePort == requestUdp.DestinationPort &&
                    p.ExtractUdp().Payload.Span.SequenceEqual(requestUdp.Payload.Span)
                , timeout);
        }

        public async Task WaitForUdpPacket(Func<IpPacket, bool> checkFunc, TimeSpan? timeout = null)
        {
            timeout ??= TimeSpan.FromSeconds(5);

            const int waitTime = 200;
            for (var elapsed = 0; elapsed < timeout.Value.TotalMilliseconds; elapsed += waitTime) {
                if (_receivedPackets.Any(checkFunc))
                    return;
                await Task.Delay(waitTime);
            }

            throw new TimeoutException(
                $"The expected UDP packet was not received. " +
                $"ReceivedPackets: {string.Join(" | ", _receivedPackets)}");
        }

        public void OnConnectionRequested(IpProtocol protocolType, IpEndPointValue remoteEndPoint)
        {
        }

        public void OnConnectionEstablished(IpProtocol protocolType, 
            IpEndPointValue localEndPoint,
            IpEndPointValue remoteEndPoint,
            bool isNewLocalEndPoint, bool isNewRemoteEndPoint)
        {
        }
    }


    [TestMethod]
    public async Task Multiple_EndPointEx()
    {
        var myPacketProxyCallbacks = new MyPacketProxyCallbacks();
        var proxyPoolOptions = TestHelper.CreateUdpProxyPoolOptions(myPacketProxyCallbacks);
        var proxyPool = new UdpProxyPool(proxyPoolOptions);
        proxyPool.PacketReceived += myPacketProxyCallbacks.PacketReceived;
        var udpEndPoint = TestHelper.WebServer.LocalEps.UdpEchoEndPoint1;
        var ipPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("127.0.0.2:2000"), udpEndPoint,
            Guid.NewGuid().ToByteArray());

        // Test
        proxyPool.SendPacketQueued(ipPacket);
        proxyPool.SendPacketQueued(ipPacket);
        await myPacketProxyCallbacks.WaitForUdpPacket(ipPacket);
        Assert.AreEqual(1, proxyPool.ClientCount);

        // -------------
        // Test
        // -------------
        ipPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("127.0.0.3:2000"), udpEndPoint,
            Guid.NewGuid().ToByteArray());
        proxyPool.SendPacketQueued(ipPacket);
        await myPacketProxyCallbacks.WaitForUdpPacket(ipPacket);
        Assert.AreEqual(2, proxyPool.ClientCount,
            "New source with a same destination should create a new worker.");

        // -------------
        // Test
        // -------------
        udpEndPoint = TestHelper.WebServer.LocalEps.UdpEchoEndPoint2;
        ipPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("127.0.0.30:2000"),
            udpEndPoint, Guid.NewGuid().ToByteArray());
        proxyPool.SendPacketQueued(ipPacket);
        await myPacketProxyCallbacks.WaitForUdpPacket(ipPacket);
        Assert.AreEqual(2, proxyPool.ClientCount,
            "New source with a new destination should not create a new worker.");

        // -------------
        // Test
        // -------------
        var udpEndPoint2 = TestHelper.WebServer.LocalEps.UdpEchoEndPoint1;
        ipPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("127.0.0.3:2000"), udpEndPoint2,
            Guid.NewGuid().ToByteArray());
        proxyPool.SendPacketQueued(ipPacket);
        await myPacketProxyCallbacks.WaitForUdpPacket(ipPacket);
        Assert.AreEqual(2, proxyPool.ClientCount,
            "new destination should not create a worker.");

        // -------------
        // Test: timeout
        // -------------
        proxyPoolOptions = TestHelper.CreateUdpProxyPoolOptions(myPacketProxyCallbacks);
        proxyPoolOptions.UdpTimeout = TimeSpan.FromSeconds(1);
        proxyPool = new UdpProxyPool(proxyPoolOptions);
        ipPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("127.0.0.2:2000"), udpEndPoint,
            Guid.NewGuid().ToByteArray());
        proxyPool.PacketReceived += myPacketProxyCallbacks.PacketReceived;
        proxyPool.SendPacketQueued(ipPacket);
        await myPacketProxyCallbacks.WaitForUdpPacket(ipPacket);
        Assert.AreEqual(1, proxyPool.ClientCount);
        await VhTestUtil.AssertEqualsWait(0, () => proxyPool.ClientCount);

        // test ip6
        udpEndPoint = TestHelper.WebServer.LocalEps.UdpEchoEndPoint1V6;
        ipPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("[::1]:2000"), udpEndPoint,
            Guid.NewGuid().ToByteArray());
        proxyPool.SendPacketQueued(ipPacket);
        await myPacketProxyCallbacks.WaitForUdpPacket(ipPacket);
    }

    [TestMethod]
    public async Task Stray_packet_should_not_kill_worker_receive_loop()
    {
        var myPacketProxyCallbacks = new MyPacketProxyCallbacks();
        var proxyPool = new UdpProxyPool(TestHelper.CreateUdpProxyPoolOptions(myPacketProxyCallbacks));
        proxyPool.PacketReceived += myPacketProxyCallbacks.PacketReceived;

        // the test owns the remote peer, so it can see the worker's endpoint and reply on demand
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var peerEndPoint = (IPEndPoint)peer.Client.LocalEndPoint!;
        var sourceEndPoint = IPEndPoint.Parse("127.0.0.2:2000");

        // establish the flow and prove the reply path works
        proxyPool.SendPacketQueued(PacketBuilder.BuildUdp(sourceEndPoint, peerEndPoint,
            Guid.NewGuid().ToByteArray()));
        var request = await peer.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await peer.SendAsync(request.Buffer, request.RemoteEndPoint);
        await myPacketProxyCallbacks.WaitForUdpPacket(p =>
            p.ExtractUdp().Payload.Span.SequenceEqual(request.Buffer));

        // blast a stray datagram at the worker from a foreign socket; the worker must drop it
        // and keep receiving for the flows that share its socket
        using var stray = new UdpClient(AddressFamily.InterNetwork);
        await stray.SendAsync(Guid.NewGuid().ToByteArray(), request.RemoteEndPoint);
        await Task.Delay(500); // let the worker process the stray packet

        // the same flow must still receive replies
        proxyPool.SendPacketQueued(PacketBuilder.BuildUdp(sourceEndPoint, peerEndPoint,
            Guid.NewGuid().ToByteArray()));
        var request2 = await peer.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var reply = Guid.NewGuid().ToByteArray();
        await peer.SendAsync(reply, request2.RemoteEndPoint);
        await myPacketProxyCallbacks.WaitForUdpPacket(p =>
            p.ExtractUdp().Payload.Span.SequenceEqual(reply));

        proxyPool.Dispose();
    }

    [TestMethod]
    public async Task Reply_after_outbound_only_period_should_be_forwarded()
    {
        // a flow that keeps sending but receives nothing must keep its reply mapping alive;
        // the mapping used to be refreshed only by inbound packets, so it expired after
        // UdpTimeout and every later reply was dropped
        var myPacketProxyCallbacks = new MyPacketProxyCallbacks();
        var proxyPoolOptions = TestHelper.CreateUdpProxyPoolOptions(myPacketProxyCallbacks);
        proxyPoolOptions.UdpTimeout = TimeSpan.FromSeconds(2);
        var proxyPool = new UdpProxyPool(proxyPoolOptions);
        proxyPool.PacketReceived += myPacketProxyCallbacks.PacketReceived;

        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var peerEndPoint = (IPEndPoint)peer.Client.LocalEndPoint!;
        var sourceEndPoint = IPEndPoint.Parse("127.0.0.2:2000");

        // send outbound-only traffic for longer than UdpTimeout
        for (var i = 0; i < 16; i++) {
            proxyPool.SendPacketQueued(PacketBuilder.BuildUdp(sourceEndPoint, peerEndPoint,
                Guid.NewGuid().ToByteArray()));
            await Task.Delay(200);
        }

        // the peer finally replies; the packet must still reach the source
        var request = await peer.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var reply = Guid.NewGuid().ToByteArray();
        await peer.SendAsync(reply, request.RemoteEndPoint);
        await myPacketProxyCallbacks.WaitForUdpPacket(p =>
            p.DestinationAddress.Equals(sourceEndPoint.Address) &&
            p.ExtractUdp().DestinationPort == sourceEndPoint.Port &&
            p.ExtractUdp().Payload.Span.SequenceEqual(reply));

        proxyPool.Dispose();
    }

    [TestMethod]
    public async Task DontFragment_transitions_should_not_break_sending()
    {
        // the DF flag maps to a per-socket setsockopt that used to be dead code; exercise both
        // transitions through a shared worker to prove activating it does not break this platform
        var myPacketProxyCallbacks = new MyPacketProxyCallbacks();
        var proxyPool = new UdpProxyPool(TestHelper.CreateUdpProxyPoolOptions(myPacketProxyCallbacks));
        proxyPool.PacketReceived += myPacketProxyCallbacks.PacketReceived;

        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var peerEndPoint = (IPEndPoint)peer.Client.LocalEndPoint!;
        var sourceEndPoint = IPEndPoint.Parse("127.0.0.2:2000");

        foreach (var dontFragment in new[] { true, false, true }) {
            var ipPacket = PacketBuilder.BuildUdp(sourceEndPoint, peerEndPoint, Guid.NewGuid().ToByteArray());
            ((IpV4Packet)ipPacket).DontFragment = dontFragment;
            proxyPool.SendPacketQueued(ipPacket);

            var request = await peer.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var reply = Guid.NewGuid().ToByteArray();
            await peer.SendAsync(reply, request.RemoteEndPoint);
            await myPacketProxyCallbacks.WaitForUdpPacket(p =>
                p.ExtractUdp().Payload.Span.SequenceEqual(reply));
        }

        proxyPool.Dispose();
    }

    [TestMethod]
    public async Task Max_UdpClients()
    {
        const int maxUdpCount = 3;

        // Create Server
        var accessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        accessManagerOptions.SessionOptions.MaxUdpClientCount = maxUdpCount;

        await using var server = await TestHelper.CreateServer(accessManagerOptions);
        var token = TestHelper.CreateAccessToken(server);

        // Create Client
        await using var client =
            await TestHelper.CreateClient(clientOptions: TestHelper.CreateClientOptions(token, channelProtocol:
                ChannelProtocol.Udp));

        // create udpClients and send packets
        var udpClients = new List<UdpClient>();
        var tasks = new List<Task>();
        for (var i = 0; i < maxUdpCount + 1; i++) {
            var udpClient = new UdpClient(AddressFamily.InterNetwork);
            udpClients.Add(udpClient);
            tasks.Add(TestHelper.Test_UdpEcho(udpClient, timeout: TimeSpan.FromSeconds(2)));
        }

        // wait for tasks to complete and not throw exceptions
        await VhUtils.TryInvokeAsync("", () => Task.WhenAll(tasks));

        // Check succeeded Udp
        Assert.AreEqual(maxUdpCount, tasks.Count(x => x.IsCompletedSuccessfully));
        Assert.AreEqual(1, tasks.Count(x => x.IsFaulted || x.IsCanceled));

        // clean up
        foreach (var udpClient in udpClients)
            udpClient.Dispose();
    }
}