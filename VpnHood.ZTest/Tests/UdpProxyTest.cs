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
    private class TestUdpProxyPool : UdpProxyPoolEx
    {
        private IPPacket? LastReceivedPacket { get; set; }
        public TestUdpProxyPool() :
            base(new TestSocketFactory(false))
        {
        }

        public override Task OnPacketReceived(IPPacket packet)
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
    }


    [TestMethod]
    public async Task Multiple_EndPoint()
    {
        var udpProxyClient = new TestUdpProxyPool();
        var udpEndPoint = TestHelper.WebServer.UdpEndPoint1;
        var udpPacket = new UdpPacket(2000, (ushort)udpEndPoint.Port)
        {
            PayloadData = Guid.NewGuid().ToByteArray()
        };

        // Test
        await udpProxyClient.SendPacket(IPAddress.Parse("127.0.0.2"),
            udpEndPoint.Address, udpPacket, false);
        await udpProxyClient.SendPacket(IPAddress.Parse("127.0.0.2"),
            udpEndPoint.Address, udpPacket, false);
        Assert.AreEqual(1, udpProxyClient.WorkerCount);
        await udpProxyClient.WaitForUdpPacket(p => p.DestinationAddress.Equals(IPAddress.Parse("127.0.0.2")));

        // Test
        udpEndPoint = TestHelper.WebServer.UdpEndPoint1;
        udpPacket = new UdpPacket(2000, (ushort)udpEndPoint.Port)
        {
            PayloadData = Guid.NewGuid().ToByteArray()
        };
        await udpProxyClient.SendPacket(IPAddress.Parse("127.0.0.3"), udpEndPoint.Address, udpPacket, false);
        Assert.AreEqual(2, udpProxyClient.WorkerCount);
        await udpProxyClient.WaitForUdpPacket(p => p.DestinationAddress.Equals(IPAddress.Parse("127.0.0.3")));

        // Test
        udpEndPoint = TestHelper.WebServer.UdpEndPoint2;
        udpPacket = new UdpPacket(2000, (ushort)udpEndPoint.Port)
        {
            PayloadData = Guid.NewGuid().ToByteArray()
        };
        await udpProxyClient.SendPacket(IPAddress.Parse("127.0.0.3"), udpEndPoint.Address, udpPacket, false);
        Assert.AreEqual(2, udpProxyClient.WorkerCount);

        //timeout
        udpProxyClient = new TestUdpProxyPool { UdpTimeout = TimeSpan.FromMicroseconds(1000) };

        udpEndPoint = TestHelper.WebServer.UdpEndPoint1;
        udpPacket = new UdpPacket(2000, (ushort)udpEndPoint.Port)
        {
            PayloadData = Guid.NewGuid().ToByteArray()
        };

        // Test
        await udpProxyClient.SendPacket(IPAddress.Parse("127.0.0.2"), udpEndPoint.Address, udpPacket, false);
        await Task.Delay(2000);
        Assert.AreEqual(0, udpProxyClient.WorkerCount);

        // test ip6
        await udpProxyClient.SendPacket(IPAddress.Parse("::1"),
            TestHelper.WebServer.UdpEndPoint2Ip6.Address, udpPacket, false);
        await udpProxyClient.WaitForUdpPacket(p => p.DestinationAddress.Equals(IPAddress.Parse("::1")));
    }

    [TestMethod]
    public async Task Multiple_EndPointEx()
    {
        var udpProxyClient = new TestUdpProxyPool();
        var ep1 = TestHelper.WebServer.UdpEndPoint1;
        var udp1 = new UdpPacket(2000, (ushort)ep1.Port) { PayloadData = Guid.NewGuid().ToByteArray() };

        // Test
        await udpProxyClient.SendPacket(IPAddress.Parse("127.0.0.2"), ep1.Address, udp1, false);
        await udpProxyClient.SendPacket(IPAddress.Parse("127.0.0.2"), ep1.Address, udp1, false);
        Assert.AreEqual(1, udpProxyClient.WorkerCount);
        await udpProxyClient.WaitForUdpPacket(p => p.DestinationAddress.Equals(IPAddress.Parse("127.0.0.2")));

        // -------------
        // Test
        // -------------
        await udpProxyClient.SendPacket(IPAddress.Parse("127.0.0.3"), ep1.Address, udp1, false);
        Assert.AreEqual(2, udpProxyClient.WorkerCount, "New source with a same destination should create a new worker.");
        await udpProxyClient.WaitForUdpPacket(p => p.DestinationAddress.Equals(IPAddress.Parse("127.0.0.3")));

        // -------------
        // Test
        // -------------
        var ep2 = TestHelper.WebServer.UdpEndPoint2;
        var udp2 = new UdpPacket(2000, (ushort)ep2.Port) { PayloadData = Guid.NewGuid().ToByteArray() };
        await udpProxyClient.SendPacket(IPAddress.Parse("127.0.0.3"), ep2.Address, udp2, false);
        Assert.AreEqual(2, udpProxyClient.WorkerCount, 
            "new destination should not create a worker.");

        // -------------
        // Test: timeout
        // -------------
        udpProxyClient = new TestUdpProxyPool { UdpTimeout = TimeSpan.FromMicroseconds(1000) };
        await udpProxyClient.SendPacket(IPAddress.Parse("127.0.0.2"), ep1.Address, udp1, false);
        Assert.AreEqual(1, udpProxyClient.WorkerCount);
        await Task.Delay(2000);
        Assert.AreEqual(0, udpProxyClient.WorkerCount);

        // test ip6
        await udpProxyClient.SendPacket(IPAddress.Parse("::1"),
            TestHelper.WebServer.UdpEndPoint2Ip6.Address, udp1, false);
        await udpProxyClient.WaitForUdpPacket(p => p.DestinationAddress.Equals(IPAddress.Parse("::1")));
    }

    [TestMethod]
    public Task Max_UdpClient()
    {
        //todo
        //throw new NotImplementedException();
        return Task.CompletedTask;
    }
}