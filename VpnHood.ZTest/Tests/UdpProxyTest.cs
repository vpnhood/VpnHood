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
    class TestUdpProxyClient : UdpProxyClient
    {
        private IPPacket? LastReceivedPacket { get; set; }
        public TestUdpProxyClient(TimeSpan? timeout) : base(new TestSocketFactory(false), timeout)
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

            var waitTime = 200;
            for (var elapsed = 0; elapsed < timeout.Value.TotalMilliseconds; elapsed += waitTime)
            {
                if (LastReceivedPacket!=null && checkFunc(LastReceivedPacket))
                    return;
                await Task.Delay(waitTime);
            }

            throw new TimeoutException();
        }
    }


    [TestMethod]
    public async Task Multiple_EndPoint()
    {
        var udpProxyClient = new TestUdpProxyClient(TimeSpan.FromSeconds(60));
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
        Assert.AreEqual(1, udpProxyClient.UdpClientCount);
        await udpProxyClient.WaitForUdpPacket(p => p.DestinationAddress.Equals(IPAddress.Parse("127.0.0.2")));

        // Test
        udpEndPoint = TestHelper.WebServer.UdpEndPoint1;
        udpPacket = new UdpPacket(2000, (ushort)udpEndPoint.Port)
        {
            PayloadData = Guid.NewGuid().ToByteArray()
        };
        await udpProxyClient.SendPacket(IPAddress.Parse("127.0.0.3"), udpEndPoint.Address, udpPacket, false);
        Assert.AreEqual(2, udpProxyClient.UdpClientCount);
        await udpProxyClient.WaitForUdpPacket(p => p.DestinationAddress.Equals(IPAddress.Parse("127.0.0.3")));

        // Test
        udpEndPoint = TestHelper.WebServer.UdpEndPoint2;
        udpPacket = new UdpPacket(2000, (ushort)udpEndPoint.Port)
        {
            PayloadData = Guid.NewGuid().ToByteArray()
        };
        await udpProxyClient.SendPacket(IPAddress.Parse("127.0.0.3"), udpEndPoint.Address, udpPacket, false);
        Assert.AreEqual(2, udpProxyClient.UdpClientCount);

        //timeout
        udpProxyClient = new TestUdpProxyClient(TimeSpan.FromSeconds(1));
        udpEndPoint = TestHelper.WebServer.UdpEndPoint1;
        udpPacket = new UdpPacket(2000, (ushort)udpEndPoint.Port)
        {
            PayloadData = Guid.NewGuid().ToByteArray()
        };

        // Test
        await udpProxyClient.SendPacket(IPAddress.Parse("127.0.0.2"),
            udpEndPoint.Address, udpPacket, false);
        await Task.Delay(1000);
        Assert.AreEqual(0, udpProxyClient.UdpClientCount);
    }
}