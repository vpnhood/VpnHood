using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PacketDotNet;
using VpnHood.Test.Factory;
using VpnHood.Tunneling;

namespace VpnHood.Test.Tests;

[TestClass]
public class UdpProxyTest
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
            bool isNewLocalEndPoint, bool isNewRemoteEndPoint) { }
    }


    [TestMethod]
    public async Task Multiple_EndPoint()
    {
        var packetProxyReceiver = new MyPacketProxyReceiver();
        var proxyPool = new UdpProxyPool(packetProxyReceiver, new TestSocketFactory(false), null, null);

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
        proxyPool = new UdpProxyPool(packetProxyReceiver, new TestSocketFactory(false), TimeSpan.FromSeconds(1), null);
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
        var proxyPool = new UdpProxyPoolEx(packetProxyReceiver, new TestSocketFactory(false), null, null);

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
        proxyPool = new UdpProxyPoolEx(packetProxyReceiver, new TestSocketFactory(false), TimeSpan.FromSeconds(1), null);
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
    public Task Max_UdpClient()
    {
        //todo
        //throw new NotImplementedException();
        return Task.CompletedTask;
    }
}