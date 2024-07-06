using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PacketDotNet;
using VpnHood.Client;
using VpnHood.Test.Services;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Utils;
using ProtocolType = PacketDotNet.ProtocolType;


namespace VpnHood.Test.Tests;

[TestClass]
public class UdpProxyTest : TestBase
{
    private class MyPacketProxyReceiver : IPacketProxyReceiver
    {
        private IPPacket? LastReceivedPacket { get; set; }
        public Task OnPacketReceived(IPPacket packet)
        {
            LastReceivedPacket = packet;
            return Task.CompletedTask;
        }

        public async Task WaitForUdpPacket(Func<IPPacket, bool> checkFunc, TimeSpan? timeout = null)
        {
            timeout ??= TimeSpan.FromSeconds(5);

            const int waitTime = 200;
            for (var elapsed = 0; elapsed < timeout.Value.TotalMilliseconds; elapsed += waitTime)
            {
                if (LastReceivedPacket != null && checkFunc(LastReceivedPacket))
                    return;
                await Task.Delay(waitTime);
            }

            throw new TimeoutException();
        }

        public void OnNewRemoteEndPoint(ProtocolType protocolType, IPEndPoint remoteEndPoint) { }

        public void OnNewEndPoint(ProtocolType protocolType, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint,
            bool isNewLocalEndPoint, bool isNewRemoteEndPoint)
        { }
    }


    [TestMethod]
    public async Task Multiple_EndPoint()
    {
        var packetProxyReceiver = new MyPacketProxyReceiver();
        var proxyPool = new UdpProxyPool(packetProxyReceiver, new TestSocketFactory(), null, null);

        // Test
        var udpEndPoint = TestHelper.WebServer.UdpV4EndPoints[0];
        var ipPacket = PacketUtil.CreateUdpPacket(IPEndPoint.Parse("127.0.0.2:2000"), udpEndPoint, Guid.NewGuid().ToByteArray());
        await proxyPool.SendPacket(ipPacket);
        Assert.AreEqual(1, proxyPool.ClientCount);
        await packetProxyReceiver.WaitForUdpPacket(p => p.DestinationAddress.Equals(IPAddress.Parse("127.0.0.2")));

        // Test
        udpEndPoint = TestHelper.WebServer.UdpV4EndPoints[0];
        ipPacket = PacketUtil.CreateUdpPacket(IPEndPoint.Parse("127.0.0.3:2000"), udpEndPoint, Guid.NewGuid().ToByteArray());
        await proxyPool.SendPacket(ipPacket);
        Assert.AreEqual(2, proxyPool.ClientCount);
        await packetProxyReceiver.WaitForUdpPacket(p => p.DestinationAddress.Equals(IPAddress.Parse("127.0.0.3")));

        // Test
        udpEndPoint = TestHelper.WebServer.UdpV4EndPoints[1];
        ipPacket = PacketUtil.CreateUdpPacket(IPEndPoint.Parse("127.0.0.3:2000"), udpEndPoint, Guid.NewGuid().ToByteArray());
        await proxyPool.SendPacket(ipPacket);
        Assert.AreEqual(2, proxyPool.ClientCount);
        await proxyPool.SendPacket(ipPacket);

        // -------------
        // Test
        // -------------
        ipPacket = PacketUtil.CreateUdpPacket(IPEndPoint.Parse("127.0.0.30:2000"), TestHelper.WebServer.UdpV4EndPoints[2], Guid.NewGuid().ToByteArray());
        await proxyPool.SendPacket(ipPacket);
        Assert.AreEqual(3, proxyPool.ClientCount,
            "New source should create a new worker.");
        await packetProxyReceiver.WaitForUdpPacket(p => p.DestinationAddress.Equals(IPAddress.Parse("127.0.0.3")));


        // Test timeout
        proxyPool = new UdpProxyPool(packetProxyReceiver, new TestSocketFactory(), TimeSpan.FromSeconds(1), null);
        udpEndPoint = TestHelper.WebServer.UdpV4EndPoints[0];
        ipPacket = PacketUtil.CreateUdpPacket(IPEndPoint.Parse("127.0.0.2:2000"), udpEndPoint, Guid.NewGuid().ToByteArray());
        await proxyPool.SendPacket(ipPacket);
        await Task.Delay(2000);
        Assert.AreEqual(0, proxyPool.ClientCount);

        // test ip6
        ipPacket = PacketUtil.CreateUdpPacket(IPEndPoint.Parse("[::1]:2000"), TestHelper.WebServer.UdpV6EndPoints[0], Guid.NewGuid().ToByteArray());
        await proxyPool.SendPacket(ipPacket);
        await packetProxyReceiver.WaitForUdpPacket(p => p.DestinationAddress.Equals(IPAddress.Parse("::1")));
    }

    [TestMethod]
    public async Task Multiple_EndPointEx()
    {
        var packetProxyReceiver = new MyPacketProxyReceiver();
        var proxyPool = new UdpProxyPoolEx(packetProxyReceiver, new TestSocketFactory(), null, null);

        var udpEndPoint = TestHelper.WebServer.UdpV4EndPoints[0];
        var ipPacket = PacketUtil.CreateUdpPacket(IPEndPoint.Parse("127.0.0.2:2000"), udpEndPoint, Guid.NewGuid().ToByteArray());

        // Test
        await proxyPool.SendPacket(ipPacket);
        await proxyPool.SendPacket(ipPacket);
        Assert.AreEqual(1, proxyPool.ClientCount);
        await packetProxyReceiver.WaitForUdpPacket(p => p.DestinationAddress.Equals(IPAddress.Parse("127.0.0.2")));

        // -------------
        // Test
        // -------------
        ipPacket = PacketUtil.CreateUdpPacket(IPEndPoint.Parse("127.0.0.3:2000"), udpEndPoint, Guid.NewGuid().ToByteArray());
        await proxyPool.SendPacket(ipPacket);
        Assert.AreEqual(2, proxyPool.ClientCount,
            "New source with a same destination should create a new worker.");
        await packetProxyReceiver.WaitForUdpPacket(p => p.DestinationAddress.Equals(IPAddress.Parse("127.0.0.3")));

        // -------------
        // Test
        // -------------
        ipPacket = PacketUtil.CreateUdpPacket(IPEndPoint.Parse("127.0.0.30:2000"), TestHelper.WebServer.UdpV4EndPoints[2], Guid.NewGuid().ToByteArray());
        await proxyPool.SendPacket(ipPacket);
        Assert.AreEqual(2, proxyPool.ClientCount,
            "New source with a new destination should not create a new worker.");
        await packetProxyReceiver.WaitForUdpPacket(p => p.DestinationAddress.Equals(IPAddress.Parse("127.0.0.3")));

        // -------------
        // Test
        // -------------
        var udpEndPoint2 = TestHelper.WebServer.UdpV4EndPoints[1];
        ipPacket = PacketUtil.CreateUdpPacket(IPEndPoint.Parse("127.0.0.3:2000"), udpEndPoint2, Guid.NewGuid().ToByteArray());
        await proxyPool.SendPacket(ipPacket);
        Assert.AreEqual(2, proxyPool.ClientCount,
            "new destination should not create a worker.");

        // -------------
        // Test: timeout
        // -------------
        proxyPool = new UdpProxyPoolEx(packetProxyReceiver, new TestSocketFactory(), TimeSpan.FromSeconds(1), null);
        ipPacket = PacketUtil.CreateUdpPacket(IPEndPoint.Parse("127.0.0.2:2000"), udpEndPoint, Guid.NewGuid().ToByteArray());
        await proxyPool.SendPacket(ipPacket);
        Assert.AreEqual(1, proxyPool.ClientCount);
        await Task.Delay(2000);
        Assert.AreEqual(0, proxyPool.ClientCount);

        // test ip6
        ipPacket = PacketUtil.CreateUdpPacket(IPEndPoint.Parse("[::1]:2000"), TestHelper.WebServer.UdpV6EndPoints[0], Guid.NewGuid().ToByteArray());
        await proxyPool.SendPacket(ipPacket);
        await packetProxyReceiver.WaitForUdpPacket(p => p.DestinationAddress.Equals(IPAddress.Parse("::1")));
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
        await using var client = await TestHelper.CreateClient(token, clientOptions: new ClientOptions { UseUdpChannel = true });

        // create udpClients and send packets
        var udpClients = new List<UdpClient>();
        var tasks = new List<Task>();
        for (var i = 0; i < maxUdpCount + 1; i++)
        {
            var udpClient = new UdpClient(AddressFamily.InterNetwork);
            udpClients.Add(udpClient);
            tasks.Add(TestHelper.Test_Udp(udpClient, TestConstants.UdpV4EndPoint1, timeout: 2000));
        }

        foreach (var task in tasks)
        {
            try { await task; }
            catch { /* Ignore */ }
        }

        // Check succeeded Udp
        Assert.AreEqual(maxUdpCount, tasks.Count(x => x.IsCompletedSuccessfully));
        Assert.AreEqual(1, tasks.Count(x => x.IsFaulted || x.IsCanceled));

        // clean up
        foreach (var udpClient in udpClients)
            udpClient.Dispose();
    }
}