using System;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PacketDotNet;
using VpnHood.Tunneling;

namespace VpnHood.Test.Tests;

[TestClass]
public class NatTest
{
    [TestMethod]
    public void Nat_NatItem_Test()
    {
        var ipPacket = PacketUtil.CreateIpPacket(IPAddress.Parse("10.1.1.1"), IPAddress.Parse("10.1.1.2"));
        var tcpPacket = new TcpPacket(100, 100);
        ipPacket.PayloadPacket = tcpPacket;

        var nat = new Nat(false);
        var id = nat.Add(ipPacket).NatId;

        // un-map
        var natItem = nat.Resolve(ipPacket.Version, ProtocolType.Tcp, id);
        Assert.IsNotNull(natItem);
        Assert.AreEqual(ipPacket.SourceAddress, natItem!.SourceAddress);
        Assert.AreEqual(tcpPacket.SourcePort, natItem.SourcePort);

        var newIpPacket = Packet.ParsePacket(LinkLayers.Raw, ipPacket.BytesSegment.Bytes).Extract<IPPacket>();
        Assert.AreEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Same NatId is expected for a same packet!");

        newIpPacket = Packet.ParsePacket(LinkLayers.Raw, ipPacket.BytesSegment.Bytes).Extract<IPPacket>();
        newIpPacket.SourceAddress = IPAddress.Parse("10.2.1.1");
        Assert.AreNotEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Different NatId is expected for a new source!");

        newIpPacket = Packet.ParsePacket(LinkLayers.Raw, ipPacket.BytesSegment.Bytes).Extract<IPPacket>();
        PacketUtil.ExtractTcp(newIpPacket).SourcePort = (ushort) (tcpPacket.SourcePort + 1);
        Assert.AreNotEqual(id, nat.GetOrAdd(newIpPacket).NatId,
            "Different NatId is expected for a new SourcePort!");

        newIpPacket = Packet.ParsePacket(LinkLayers.Raw, ipPacket.BytesSegment.Bytes).Extract<IPPacket>();
        newIpPacket.DestinationAddress = IPAddress.Parse("10.2.1.1");
        Assert.AreEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Same NatId is expected for a new destination!");

        newIpPacket = Packet.ParsePacket(LinkLayers.Raw, ipPacket.BytesSegment.Bytes).Extract<IPPacket>();
        PacketUtil.ExtractTcp(newIpPacket).DestinationPort = (ushort) (tcpPacket.DestinationPort + 1);
        Assert.AreEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Sme NatId is expected for a new destinationPort!");
    }

    [TestMethod]
    public void Nat_NatItemEx_Test()
    {
        var nat = new Nat(true);

        var ipPacket = PacketUtil.CreateIpPacket(IPAddress.Parse("10.1.1.1"), IPAddress.Parse("10.1.1.2"));
        var tcpPacket = new TcpPacket(100, 100);
        ipPacket.PayloadPacket = tcpPacket;
        var id = nat.Add(ipPacket).NatId;

        var ipPacket2 = PacketUtil.CreateIpPacket(IPAddress.Parse("10.1.1.1"), IPAddress.Parse("10.1.1.2"));
        var tcpPacket2 = new TcpPacket(101, 100);
        ipPacket2.PayloadPacket = tcpPacket2;
        nat.Add(ipPacket2);

        // un-map
        var natItem = (NatItemEx?) nat.Resolve(ipPacket.Version, ProtocolType.Tcp, id);
        Assert.IsNotNull(natItem);
        Assert.AreEqual(ipPacket.SourceAddress, natItem!.SourceAddress);
        Assert.AreEqual(ipPacket.DestinationAddress, natItem.DestinationAddress);
        Assert.AreEqual(tcpPacket.SourcePort, natItem.SourcePort);
        Assert.AreEqual(tcpPacket.DestinationPort, natItem.DestinationPort);

        var newIpPacket = Packet.ParsePacket(LinkLayers.Raw, ipPacket.BytesSegment.Bytes).Extract<IPPacket>();
        Assert.AreEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Same NatId is expected for a same packet!");

        newIpPacket = Packet.ParsePacket(LinkLayers.Raw, ipPacket.BytesSegment.Bytes).Extract<IPPacket>();
        newIpPacket.SourceAddress = IPAddress.Parse("10.2.1.1");
        Assert.AreNotEqual(id, nat.GetOrAdd(newIpPacket).NatId, "Different NatId is expected for a new source!");

        newIpPacket = Packet.ParsePacket(LinkLayers.Raw, ipPacket.BytesSegment.Bytes).Extract<IPPacket>();
        PacketUtil.ExtractTcp(newIpPacket).DestinationPort = (ushort) (tcpPacket.SourcePort + 1);
        Assert.AreNotEqual(id, nat.GetOrAdd(newIpPacket).NatId,
            "Different NatId is expected for a new SourcePort!");

        newIpPacket = Packet.ParsePacket(LinkLayers.Raw, ipPacket.BytesSegment.Bytes).Extract<IPPacket>();
        newIpPacket.DestinationAddress = IPAddress.Parse("10.2.1.1");
        Assert.AreNotEqual(id, nat.GetOrAdd(newIpPacket).NatId,
            "Different NatId is expected for a new destination!");

        newIpPacket = Packet.ParsePacket(LinkLayers.Raw, ipPacket.BytesSegment.Bytes).Extract<IPPacket>();
        PacketUtil.ExtractTcp(newIpPacket).SourcePort = (ushort) (tcpPacket.DestinationPort + 1);
        Assert.AreNotEqual(id, nat.GetOrAdd(newIpPacket).NatId,
            "Different NatId is expected for a new destinationPort!");
    }

    [TestMethod]
    public void Nat_OverFlow_Test()
    {
        var ipPacket = PacketUtil.CreateIpPacket(IPAddress.Parse("10.1.1.1"), IPAddress.Parse("10.1.1.2"));
        var tcpPacket = new TcpPacket(100, 100);
        ipPacket.PayloadPacket = tcpPacket;

        var nat = new Nat(true);

        // fill NAT
        for (var i = 1; i < 0xFFFF; i++)
        {
            tcpPacket.SourcePort = (ushort) i;
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
        catch (OverflowException)
        {
        }
    }
}