using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PacketDotNet;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.VhPackets;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Test.Tests;

[TestClass]
public class PacketTest : TestBase
{
    [TestMethod]
    public void Udp()
    {
        var ipPacket = IpPacketFactory.BuildUdp(
            sourceEndPoint:  IPEndPoint.Parse("11.12.13.14:50"),
            destinationEndPoint: IPEndPoint.Parse("21.22.23.24:60"),
            payload: [0,1,2,3,4,5]);
        var udpPacket = ipPacket.ExtractUdp();
        ipPacket.UpdateAllChecksums();

        // check with PacketDotNet
        var packet2 = PacketBuilder.Parse(ipPacket.Buffer.ToArray());
        packet2.UpdateAllChecksums();
        var udpPacket2 = packet2.ExtractUdp();
        Assert.AreEqual(udpPacket2.Length, udpPacket.Buffer.Length);
        Assert.AreEqual(udpPacket2.SourcePort, udpPacket.SourcePort);
        Assert.AreEqual(udpPacket2.DestinationPort, udpPacket.DestinationPort);
        Assert.AreEqual(udpPacket2.Checksum, udpPacket.Checksum);
    }

    [TestMethod]
    public void IP_Addresses()
    {
        var sourceAddress = IPAddress.Parse("11.12.13.14");
        var destinationAddress = IPAddress.Parse("21.22.23.24");

        // Test ip addresses change by changing VhIpPacket.SourceAddress
        var ipV4Packet = new VhIpV4Packet(new byte[40], VhIpProtocol.Raw, 0) {
            SourceAddress = sourceAddress,
            DestinationAddress = destinationAddress
        };

        Assert.AreEqual(sourceAddress, ipV4Packet.SourceAddress);
        Assert.AreEqual(destinationAddress, ipV4Packet.DestinationAddress);
        CollectionAssert.AreEqual(sourceAddress.GetAddressBytes(), ipV4Packet.SourceAddressSpan.ToArray());
        CollectionAssert.AreEqual(destinationAddress.GetAddressBytes(), ipV4Packet.DestinationAddressSpan.ToArray());

        // Test ip addresses change by changing VhIpPacket.SourceAddressSpan 
        ipV4Packet = new VhIpV4Packet(new byte[40], VhIpProtocol.Raw, 0);
        sourceAddress.GetAddressBytes().CopyTo(ipV4Packet.SourceAddressSpan);
        destinationAddress.GetAddressBytes().CopyTo(ipV4Packet.DestinationAddressSpan);
        Assert.AreEqual(sourceAddress, ipV4Packet.SourceAddress);
        Assert.AreEqual(destinationAddress, ipV4Packet.DestinationAddress);
        CollectionAssert.AreEqual(sourceAddress.GetAddressBytes(), ipV4Packet.SourceAddressSpan.ToArray());
        CollectionAssert.AreEqual(destinationAddress.GetAddressBytes(), ipV4Packet.DestinationAddressSpan.ToArray());
    }

    [TestMethod]
    public void IPv4()
    {
        var sourceIp = IPAddress.Parse("1.2.3.4");
        var destinationIp = IPAddress.Parse("4.3.2.1");
        byte ttl = 126;
        byte fragmentFlags = 2;
        ushort id = 0xFFEE;
        var offset = 0xAFF;
        // ReSharper disable once IdentifierTypo
        byte dscp = 0; //
        var ecn = IpEcnField.NonEct;

        var packet = (IPv4Packet)PacketBuilder.BuildIpPacket(sourceIp, destinationIp);
        packet.Protocol = ProtocolType.Raw;
        packet.TimeToLive = ttl;
        packet.Version = IPVersion.IPv4;
        packet.FragmentFlags = fragmentFlags;
        packet.FragmentOffset = offset;
        packet.Id = id;
        packet.DifferentiatedServices = dscp;
        packet.UpdateAllChecksums();

        VhLogger.Instance.LogDebug("Assert read from buffer.");
        var ipPacket = new VhIpV4Packet(packet.Bytes);
        Assert.AreEqual(VhIpVersion.IPv4, ipPacket.Version);
        Assert.AreEqual(sourceIp, ipPacket.SourceAddress);
        Assert.AreEqual(destinationIp, ipPacket.DestinationAddress);
        Assert.AreEqual(VhIpProtocol.Raw, ipPacket.Protocol);
        Assert.AreEqual(ttl, ipPacket.TimeToLive);
        Assert.AreEqual(fragmentFlags, ipPacket.FragmentFlags);
        Assert.AreEqual(offset, ipPacket.FragmentOffset);
        Assert.AreEqual(id, ipPacket.Identification);
        Assert.AreEqual(dscp, ipPacket.Dscp);
        Assert.AreEqual(ecn, ipPacket.Ecn);
        Assert.AreEqual(0, ipPacket.Payload.Length);

        VhLogger.Instance.LogDebug("Assert changes");
        sourceIp = IPAddress.Parse("21.22.23.24");
        destinationIp = IPAddress.Parse("34.33.32.31");
        ttl = 127;
        fragmentFlags = 3;
        id = 0xFFEA;
        offset = 0xAFA;
        dscp = 19;
        ecn = IpEcnField.Ce;

        ipPacket.SourceAddress = sourceIp;
        ipPacket.DestinationAddress = destinationIp;
        ipPacket.TimeToLive = ttl;
        ipPacket.FragmentFlags = fragmentFlags;
        ipPacket.FragmentOffset = offset;
        ipPacket.Identification = id;
        ipPacket.Dscp = dscp;
        ipPacket.Ecn = ecn;

        VhLogger.Instance.LogDebug("Assert read from buffer.");
        ipPacket = new VhIpV4Packet(ipPacket.Buffer.ToArray());
        Assert.AreEqual(VhIpVersion.IPv4, ipPacket.Version);
        Assert.AreEqual(VhIpProtocol.Raw, ipPacket.Protocol);
        Assert.AreEqual(sourceIp, ipPacket.SourceAddress);
        Assert.AreEqual(destinationIp, ipPacket.DestinationAddress);
        Assert.AreEqual(VhIpProtocol.Raw, ipPacket.Protocol);
        Assert.AreEqual(ttl, ipPacket.TimeToLive);
        Assert.AreEqual(fragmentFlags, ipPacket.FragmentFlags);
        Assert.AreEqual(offset, ipPacket.FragmentOffset);
        Assert.AreEqual(id, ipPacket.Identification);
        Assert.AreEqual(0, ipPacket.Payload.Length);
    }

    [TestMethod]
    public void IPv6()
    {
        var sourceIp = IPAddress.Parse("2001:db8::1");
        var destinationIp = IPAddress.Parse("2001:db8::2");
        byte ttl = 126;
        var flowLabel = 0x12345;
        const VhIpProtocol nextHeader = VhIpProtocol.IPv6NoNextHeader;
        byte trafficClass = 0xFF;
        var packet = (IPv6Packet)PacketBuilder.BuildIpPacket(sourceIp, destinationIp);
        
        packet.HopLimit = ttl;
        packet.Version = IPVersion.IPv6;
        packet.FlowLabel = flowLabel;
        packet.TrafficClass = trafficClass;

        VhLogger.Instance.LogDebug("Assert read from buffer.");
        var ipPacket = new VhIpV6Packet(packet.Bytes);
        Assert.AreEqual(VhIpVersion.IPv6, ipPacket.Version);
        Assert.AreEqual(nextHeader, ipPacket.Protocol);
        Assert.AreEqual(nextHeader, ipPacket.NextHeader);
        Assert.AreEqual(sourceIp, ipPacket.SourceAddress);
        Assert.AreEqual(destinationIp, ipPacket.DestinationAddress);
        Assert.AreEqual(ttl, ipPacket.TimeToLive);
        Assert.AreEqual(ttl, ipPacket.HopLimit);
        Assert.AreEqual(flowLabel, ipPacket.FlowLabel);
        Assert.AreEqual(trafficClass, ipPacket.TrafficClass);

        VhLogger.Instance.LogDebug("Assert changes");
        sourceIp = IPAddress.Parse("2001:db8::3");
        destinationIp = IPAddress.Parse("2001:db8::4");
        ttl = 127;
        flowLabel = 0x54321;
        trafficClass = 0xAA;
        ipPacket.SourceAddress = sourceIp;
        ipPacket.DestinationAddress = destinationIp;
        ipPacket.TimeToLive = ttl;
        ipPacket.HopLimit = ttl;
        ipPacket.FlowLabel = flowLabel;
        ipPacket.TrafficClass = trafficClass;
        VhLogger.Instance.LogDebug("Assert read from buffer.");
        ipPacket = new VhIpV6Packet(ipPacket.Buffer.ToArray());
        Assert.AreEqual(VhIpVersion.IPv6, ipPacket.Version);
        Assert.AreEqual(nextHeader, ipPacket.Protocol);
        Assert.AreEqual(nextHeader, ipPacket.NextHeader);
        Assert.AreEqual(sourceIp, ipPacket.SourceAddress);
        Assert.AreEqual(destinationIp, ipPacket.DestinationAddress);
        Assert.AreEqual(ttl, ipPacket.TimeToLive);
        Assert.AreEqual(ttl, ipPacket.HopLimit);
    }

    [TestMethod]
    public void SimplePacket()
    {
        var a = PacketBuilder.BuildUdpPacket(
            IPEndPoint.Parse("1.2.3.4:8080"),
            IPEndPoint.Parse("11.12.13.14:90"),
            [1, 2, 3]);

        var ipPacket = new VhIpV4Packet(a.Bytes);
        Console.WriteLine(ipPacket);

        var udp = ipPacket.ExtractUdp();
        Console.WriteLine(udp.Checksum);
        var cc = udp.ComputeChecksum(ipPacket);
        Console.WriteLine(cc);
    }
}