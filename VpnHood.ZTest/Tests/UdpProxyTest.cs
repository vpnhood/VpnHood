using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PacketDotNet;
using VpnHood.Server;
using VpnHood.Server.Providers.FileAccessServerProvider;
using VpnHood.Test.Factory;
using VpnHood.Tunneling;

namespace VpnHood.Test.Tests;

[TestClass]
public class UdpProxyTest
{
    class TestUdpProxyPool : UdpProxyPool
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

            var waitTime = 200;
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
        udpProxyClient = new TestUdpProxyPool { UdpTimeout = TimeSpan.FromSeconds(1) };

        udpEndPoint = TestHelper.WebServer.UdpEndPoint1;
        udpPacket = new UdpPacket(2000, (ushort)udpEndPoint.Port)
        {
            PayloadData = Guid.NewGuid().ToByteArray()
        };

        // Test
        await udpProxyClient.SendPacket(IPAddress.Parse("127.0.0.2"),
            udpEndPoint.Address, udpPacket, false);
        await Task.Delay(1000);
        Assert.AreEqual(0, udpProxyClient.WorkerCount);
    }

    [TestMethod]
    public Task Max_UdpClient()
    {
        var fileAccessServerOptions = TestHelper.CreateFileAccessServerOptions();
        fileAccessServerOptions.SessionOptions.MaxUdpPortCount = 2;
        using var server = TestHelper.CreateServer();
        throw new NotImplementedException();
    }
}