using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net;
using PacketDotNet;
using VpnHood.Tunneling;

namespace VpnHood.Test
{
    [TestClass]
    public class Nat_Test
    {
        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext _)
        {
        }

        [AssemblyCleanup()]
        public static void AssemblyCleanup()
        {
            TestHelper.Cleanup();
        }

        [TestMethod]
        public void Nat_NatItem_Test()
        {
            var ipPacket = new IPv4Packet(IPAddress.Parse("10.1.1.1"), IPAddress.Parse("10.1.1.2"));
            var tcpPacket = new TcpPacket(100, 100);
            ipPacket.PayloadPacket = tcpPacket;

            var nat = new Nat(false);
            var id = nat.Add(ipPacket).NatId;

            // unmap
            var natItem = nat.Resolve(ProtocolType.Tcp, id);
            Assert.IsNotNull(natItem);
            Assert.AreEqual(ipPacket.SourceAddress, natItem.SourceAddress);
            Assert.AreEqual(tcpPacket.SourcePort, natItem.SourcePort);

            var newIpPacket = new IPv4Packet(ipPacket.BytesSegment);
            Assert.AreEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Same NatId is expected for a sampe packet!");

            newIpPacket = new IPv4Packet(ipPacket.BytesSegment) { SourceAddress = IPAddress.Parse("10.2.1.1") };
            Assert.AreNotEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Different NatId is expected for a new source!");

            newIpPacket = new IPv4Packet(ipPacket.BytesSegment);
            newIpPacket.Extract<TcpPacket>().SourcePort = (ushort)(tcpPacket.SourcePort + 1);
            Assert.AreNotEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Different NatId is expected for a new SourcePort!");

            newIpPacket = new IPv4Packet(ipPacket.BytesSegment) { DestinationAddress = IPAddress.Parse("10.2.1.1") };
            Assert.AreEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Same NatId is expected for a new destination!");

            newIpPacket = new IPv4Packet(ipPacket.BytesSegment);
            newIpPacket.Extract<TcpPacket>().DestinationPort = (ushort)(tcpPacket.DestinationPort + 1);
            Assert.AreEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Sme NatId is expected for a new destinationPort!");

        }

        [TestMethod]
        public void Nat_NatItemEx_Test()
        {
            var ipPacket = new IPv4Packet(IPAddress.Parse("10.1.1.1"), IPAddress.Parse("10.1.1.2"));
            var tcpPacket = new TcpPacket(100, 100);
            ipPacket.PayloadPacket = tcpPacket;

            var nat = new Nat(true);
            var id = nat.Add(ipPacket).NatId;

            // unmap
            var natItem = (NatItemEx)nat.Resolve(ProtocolType.Tcp, id);
            Assert.IsNotNull(natItem);
            Assert.AreEqual(ipPacket.SourceAddress, natItem.SourceAddress);
            Assert.AreEqual(ipPacket.DestinationAddress, natItem.DestinationAddress);
            Assert.AreEqual(tcpPacket.SourcePort, natItem.SourcePort);
            Assert.AreEqual(tcpPacket.DestinationPort, natItem.DestinationPort);

            var newIpPacket = new IPv4Packet(ipPacket.BytesSegment);
            Assert.AreEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Same NatId is expected for a sampe packet!");

            newIpPacket = new IPv4Packet(ipPacket.BytesSegment) { SourceAddress = IPAddress.Parse("10.2.1.1") };
            Assert.AreNotEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Different NatId is expected for a new source!");

            newIpPacket = new IPv4Packet(ipPacket.BytesSegment);
            newIpPacket.Extract<TcpPacket>().DestinationPort = (ushort)(tcpPacket.SourcePort + 1);
            Assert.AreNotEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Different NatId is expected for a new SourcePort!");

            newIpPacket = new IPv4Packet(ipPacket.BytesSegment) { DestinationAddress = IPAddress.Parse("10.2.1.1") };
            Assert.AreNotEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Different NatId is expected for a new destination!");

            newIpPacket = new IPv4Packet(ipPacket.BytesSegment);
            newIpPacket.Extract<TcpPacket>().SourcePort = (ushort)(tcpPacket.DestinationPort + 1);
            Assert.AreNotEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Different NatId is expected for a new destinationPort!");
        }

        [TestMethod]
        public void Nat_OverFlow_Test()
        {
            var ipPacket = new IPv4Packet(IPAddress.Parse("10.1.1.1"), IPAddress.Parse("10.1.1.2"));
            var tcpPacket = new TcpPacket(100, 100);
            ipPacket.PayloadPacket = tcpPacket;

            var nat = new Nat(true);

            // fill NAT
            for (var i = 1; i < 0xFFFF; i++)
            {
                tcpPacket.SourcePort = (ushort)i;
                nat.Add(ipPacket);
            }

            // test exception (port is full)
            ipPacket.SourceAddress = IPAddress.Parse("10.1.1.2");
            tcpPacket.SourcePort = 2;
            nat.Add(ipPacket);

            try
            {
                tcpPacket.SourcePort = 3;
                nat.Add(ipPacket);
                Assert.Fail("Exception expected!");
            }
            catch (OverflowException) { }
        }
    }
}
