using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Proxies;

namespace VpnHood.Test.Tests;

[TestClass]
public class UdpProxyTest : TestBase
{
    private class MyPacketProxyCallbacks : IPacketProxyCallbacks
    {
        private IpPacket? LastReceivedPacket { get; set; }

        public void PacketReceived(object? sender, IpPacket ipPacket)
        { 
            LastReceivedPacket = ipPacket;
        }

        public Task WaitForUdpPacket(IpPacket ipPacket, TimeSpan? timeout = null)
        {
            return WaitForUdpPacket(p => 
                p.DestinationAddress.Equals(ipPacket.SourceAddress) &&
                p.ExtractUdp().DestinationPort == ipPacket.ExtractUdp().SourcePort &&
                p.SourceAddress.Equals(ipPacket.DestinationAddress) &&
                p.ExtractUdp().SourcePort == ipPacket.ExtractUdp().DestinationPort
                , timeout);
        }
        
        public async Task WaitForUdpPacket(Func<IpPacket, bool> checkFunc, TimeSpan? timeout = null)
        {
            timeout ??= TimeSpan.FromSeconds(5);

            const int waitTime = 200;
            for (var elapsed = 0; elapsed < timeout.Value.TotalMilliseconds; elapsed += waitTime) {
                if (LastReceivedPacket != null && checkFunc(LastReceivedPacket))
                    return;
                await Task.Delay(waitTime);
            }

            throw new TimeoutException();
        }

        public void OnConnectionRequested(IpProtocol protocolType, IPEndPoint remoteEndPoint)
        {
        }

        public void OnConnectionEstablished(IpProtocol protocolType, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint,
            bool isNewLocalEndPoint, bool isNewRemoteEndPoint)
        {
        }
    }

    
    [TestMethod]
    public async Task Multiple_EndPoint()
    {
        var myPacketProxyCallbacks = new MyPacketProxyCallbacks();
        var proxyPool = new UdpProxyPool(TestHelper.CreateUdpProxyPoolOptions(myPacketProxyCallbacks));
        proxyPool.PacketReceived += myPacketProxyCallbacks.PacketReceived;

        // Test
        var udpEndPoint = TestHelper.WebServer.UdpV4EndPoints[0];
        var ipPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("127.0.0.2:2000"), udpEndPoint,
            Guid.NewGuid().ToByteArray());
        proxyPool.SendPacketQueued(ipPacket);
        await myPacketProxyCallbacks.WaitForUdpPacket(ipPacket);
        Assert.AreEqual(1, proxyPool.ClientCount);

        // Test
        udpEndPoint = TestHelper.WebServer.UdpV4EndPoints[0];
        ipPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("127.0.0.3:2000"), udpEndPoint,
            Guid.NewGuid().ToByteArray());
        proxyPool.SendPacketQueued(ipPacket);
        await myPacketProxyCallbacks.WaitForUdpPacket(ipPacket);
        Assert.AreEqual(2, proxyPool.ClientCount);

        // Test
        udpEndPoint = TestHelper.WebServer.UdpV4EndPoints[1];
        ipPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("127.0.0.3:2000"), udpEndPoint,
            Guid.NewGuid().ToByteArray());
        proxyPool.SendPacketQueued(ipPacket);
        await myPacketProxyCallbacks.WaitForUdpPacket(ipPacket);
        Assert.AreEqual(2, proxyPool.ClientCount);
        proxyPool.SendPacketQueued(ipPacket);

        // Test
        ipPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("127.0.0.30:2000"),
            TestHelper.WebServer.UdpV4EndPoints[2], Guid.NewGuid().ToByteArray());
        proxyPool.SendPacketQueued(ipPacket);
        await myPacketProxyCallbacks.WaitForUdpPacket(ipPacket);
        Assert.AreEqual(3, proxyPool.ClientCount, "New source should create a new worker.");

        // Test timeout
        var proxyPoolOptions = TestHelper.CreateUdpProxyPoolOptions(myPacketProxyCallbacks);
        proxyPoolOptions.UdpTimeout = TimeSpan.FromSeconds(1);
        proxyPool = new UdpProxyPool(proxyPoolOptions);

        proxyPool.PacketReceived += myPacketProxyCallbacks.PacketReceived;
        udpEndPoint = TestHelper.WebServer.UdpV4EndPoints[0];
        ipPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("127.0.0.2:2000"), udpEndPoint,
            Guid.NewGuid().ToByteArray());
        proxyPool.SendPacketQueued(ipPacket);
        await myPacketProxyCallbacks.WaitForUdpPacket(ipPacket);
        await VhTestUtil.AssertEqualsWait(0, ()=> proxyPool.ClientCount);

        // test ip6
        ipPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("[::1]:2000"), TestHelper.WebServer.UdpV6EndPoints[0],
            Guid.NewGuid().ToByteArray());
        proxyPool.SendPacketQueued(ipPacket);
        await myPacketProxyCallbacks.WaitForUdpPacket(ipPacket);
        await myPacketProxyCallbacks.WaitForUdpPacket(p => p.DestinationAddress.Equals(IPAddress.Parse("::1")));
    }

    [TestMethod]
    public async Task Multiple_EndPointEx()
    {
        var myPacketProxyCallbacks = new MyPacketProxyCallbacks();
        var proxyPoolOptions = TestHelper.CreateUdpProxyPoolOptions(myPacketProxyCallbacks);
        var proxyPool = new UdpProxyPoolEx(proxyPoolOptions);
        proxyPool.PacketReceived += myPacketProxyCallbacks.PacketReceived;
        var udpEndPoint = TestHelper.WebServer.UdpV4EndPoints[0];
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
        ipPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("127.0.0.30:2000"),
            TestHelper.WebServer.UdpV4EndPoints[2], Guid.NewGuid().ToByteArray());
        proxyPool.SendPacketQueued(ipPacket);
        await myPacketProxyCallbacks.WaitForUdpPacket(ipPacket);
        Assert.AreEqual(2, proxyPool.ClientCount,
            "New source with a new destination should not create a new worker.");

        // -------------
        // Test
        // -------------
        var udpEndPoint2 = TestHelper.WebServer.UdpV4EndPoints[1];
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
        proxyPool = new UdpProxyPoolEx(proxyPoolOptions);
        ipPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("127.0.0.2:2000"), udpEndPoint, Guid.NewGuid().ToByteArray());
        proxyPool.PacketReceived += myPacketProxyCallbacks.PacketReceived;
        proxyPool.SendPacketQueued(ipPacket);
        await myPacketProxyCallbacks.WaitForUdpPacket(ipPacket);
        Assert.AreEqual(1, proxyPool.ClientCount);
        await VhTestUtil.AssertEqualsWait(0, ()=> proxyPool.ClientCount);

        // test ip6
        ipPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("[::1]:2000"), TestHelper.WebServer.UdpV6EndPoints[0],
            Guid.NewGuid().ToByteArray());
        proxyPool.SendPacketQueued(ipPacket);
        await myPacketProxyCallbacks.WaitForUdpPacket(ipPacket);
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
            await TestHelper.CreateClient(clientOptions: TestHelper.CreateClientOptions(token, useUdpChannel: true));

        // create udpClients and send packets
        var udpClients = new List<UdpClient>();
        var tasks = new List<Task>();
        for (var i = 0; i < maxUdpCount + 1; i++) {
            var udpClient = new UdpClient(AddressFamily.InterNetwork);
            udpClients.Add(udpClient);
            tasks.Add(TestHelper.Test_Udp(udpClient, TestConstants.UdpV4EndPoint1, timeout: TimeSpan.FromSeconds(2)));
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