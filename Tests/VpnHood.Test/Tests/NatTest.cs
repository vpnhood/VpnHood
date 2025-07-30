using System.Net;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Tunneling;

namespace VpnHood.Test.Tests;

[TestClass]
public class NatTest : TestBase
{
    [TestMethod]
    public void Nat_NatItem_Test()
    {
        var ipPacket = PacketBuilder.BuildTcp(IPEndPoint.Parse("10.1.1.1:100"), IPEndPoint.Parse("10.1.1.2:100"), null, null);
        var tcpPacket = ipPacket.ExtractTcp();

        var nat = new Nat(false);
        var id = nat.Add(ipPacket).NatId;

        // un-map
        var natItem = nat.Resolve(ipPacket.Version, IpProtocol.Tcp, id);
        Assert.IsNotNull(natItem);
        Assert.AreEqual(ipPacket.SourceAddress, natItem.SourceAddress);
        Assert.AreEqual(tcpPacket.SourcePort, natItem.SourcePort);

        var newIpPacket = ipPacket.Clone();
        Assert.AreEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Same NatId is expected for a same packet!");

        newIpPacket = ipPacket.Clone();
        newIpPacket.SourceAddress = IPAddress.Parse("10.2.1.1");
        Assert.AreNotEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Different NatId is expected for a new source!");

        newIpPacket = ipPacket.Clone();
        newIpPacket.ExtractTcp().SourcePort = (ushort)(tcpPacket.SourcePort + 1);
        Assert.AreNotEqual(id, nat.GetOrAdd(newIpPacket).NatId,
            "Different NatId is expected for a new SourcePort!");

        newIpPacket = ipPacket.Clone();
        newIpPacket.DestinationAddress = IPAddress.Parse("10.2.1.1");
        Assert.AreEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Same NatId is expected for a new destination!");

        newIpPacket = ipPacket.Clone();
        newIpPacket.ExtractTcp().DestinationPort = (ushort)(tcpPacket.DestinationPort + 1);
        Assert.AreEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Sme NatId is expected for a new destinationPort!");
    }

    [TestMethod]
    public void Nat_NatItemEx_Test()
    {
        var nat = new Nat(true);

        var ipPacket = PacketBuilder.BuildTcp(IPEndPoint.Parse("10.1.1.1:100"), IPEndPoint.Parse("10.1.1.2:100"), null, null);
        var tcpPacket = ipPacket.ExtractTcp();
        var id = nat.Add(ipPacket).NatId;

        var ipPacket2 = PacketBuilder.BuildTcp(IPEndPoint.Parse("10.1.1.1:101"), IPEndPoint.Parse("10.1.1.2:100"), null, null);
        nat.Add(ipPacket2);

        // un-map
        var natItem = (NatItemEx?)nat.Resolve(ipPacket.Version, IpProtocol.Tcp, id);
        Assert.IsNotNull(natItem);
        Assert.AreEqual(ipPacket.SourceAddress, natItem.SourceAddress);
        Assert.AreEqual(ipPacket.DestinationAddress, natItem.DestinationAddress);
        Assert.AreEqual(tcpPacket.SourcePort, natItem.SourcePort);
        Assert.AreEqual(tcpPacket.DestinationPort, natItem.DestinationPort);

        var newIpPacket = ipPacket.Clone();
        Assert.AreEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Same NatId is expected for a same packet!");

        newIpPacket = ipPacket.Clone();
        newIpPacket.SourceAddress = IPAddress.Parse("10.2.1.1");
        Assert.AreNotEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Different NatId is expected for a new source!");

        newIpPacket = ipPacket.Clone();
        newIpPacket.ExtractTcp().DestinationPort = (ushort)(tcpPacket.SourcePort + 1);
        Assert.AreNotEqual(id, nat.GetOrAdd(newIpPacket).NatId,
            "Different NatId is expected for a new SourcePort!");

        newIpPacket = ipPacket.Clone();
        newIpPacket.DestinationAddress = IPAddress.Parse("10.2.1.1");
        Assert.AreNotEqual(id, nat.GetOrAdd(newIpPacket).NatId,
            "Different NatId is expected for a new destination!");

        newIpPacket = ipPacket.Clone();
        newIpPacket.ExtractTcp().SourcePort = (ushort)(tcpPacket.DestinationPort + 1);
        Assert.AreNotEqual(id, nat.GetOrAdd(newIpPacket).NatId,
            "Different NatId is expected for a new destinationPort!");
    }

    [TestMethod]
    public void Nat_OverFlow_Test()
    {
        var ipPacket = PacketBuilder.BuildTcp(IPEndPoint.Parse("10.1.1.1:100"), IPEndPoint.Parse("10.1.1.2:100"), null, null);
        var tcpPacket = ipPacket.ExtractTcp();
        var nat = new Nat(true);

        // fill NAT
        for (var i = 1; i < 0xFFFF; i++) {
            tcpPacket.SourcePort = (ushort)i;
            nat.Add(ipPacket);
        }

        // test exception (port is full)
        ipPacket.SourceAddress = IPAddress.Parse("10.1.1.2");
        tcpPacket.SourcePort = 2;
        nat.Add(ipPacket);

        try {
            tcpPacket.SourcePort = 3;
            nat.Add(ipPacket);
            Assert.Fail("Exception expected!");
        }
        catch (OverflowException) {
        }
    }
}