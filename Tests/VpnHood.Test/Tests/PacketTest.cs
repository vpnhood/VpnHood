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
    private static IPAddress GetRandomIp(VhIpVersion ipVersion)
    {
        var random = new Random();
        var buffer = new byte[ipVersion == VhIpVersion.IPv4 ? 4 : 16];
        random.NextBytes(buffer);
        return new IPAddress(buffer);
    }

    private static IPEndPoint GetRandomEp(VhIpVersion ipVersion) =>
        new(GetRandomIp(ipVersion), Random.Shared.Next(0xFFFF));

    [TestMethod]
    [DataRow(VhIpVersion.IPv4)]
    [DataRow(VhIpVersion.IPv6)]
    public void Udp(VhIpVersion ipVersion)
    {
        var ipPacket = IpPacketFactory.BuildUdp(
            sourceEndPoint: GetRandomEp(ipVersion),
            destinationEndPoint: GetRandomEp(ipVersion),
            payload: [0, 1, 2, 3, 4, 5]);
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
        CollectionAssert.AreEqual(udpPacket2.Bytes, udpPacket.Buffer.ToArray());

        // Parse
        ipPacket.Dispose();
        ipPacket = IpPacketFactory.Parse(packet2.Bytes);
        udpPacket = ipPacket.ExtractUdp();
        Assert.AreEqual(udpPacket2.Length, udpPacket.Buffer.Length);
        Assert.AreEqual(udpPacket2.SourcePort, udpPacket.SourcePort);
        Assert.AreEqual(udpPacket2.DestinationPort, udpPacket.DestinationPort);
        Assert.AreEqual(udpPacket2.Checksum, udpPacket.Checksum);
        CollectionAssert.AreEqual(udpPacket2.Bytes, udpPacket.Buffer.ToArray());
        ipPacket.Dispose();
    }

    [TestMethod]
    [DataRow(VhIpVersion.IPv4, true)]
    [DataRow(VhIpVersion.IPv4, false)]
    [DataRow(VhIpVersion.IPv6, true)]
    public void Tcp(VhIpVersion ipVersion, bool mode)
    {
        var ipPacket = IpPacketFactory.BuildTcp(
            sourceEndPoint: GetRandomEp(ipVersion),
            destinationEndPoint: GetRandomEp(ipVersion),
            [], payload: [0, 1, 2, 3, 4, 5]);
        var tcpPacket = ipPacket.ExtractTcp();
        tcpPacket.Acknowledgment = mode;
        tcpPacket.AcknowledgmentNumber = 0x1234;
        tcpPacket.SequenceNumber = 0x5678;
        tcpPacket.Reset = !mode;
        tcpPacket.Synchronize = mode;
        tcpPacket.Acknowledgment = !mode;
        tcpPacket.Push = mode;
        tcpPacket.WindowSize = 0xBBBB;
        tcpPacket.UrgentPointer = 0x10;
        ipPacket.UpdateAllChecksums();

        // check with PacketDotNet
        var packet2 = PacketBuilder.Parse(ipPacket.Buffer.ToArray());
        packet2.UpdateAllChecksums();
        var udpPacket2 = packet2.ExtractTcp();
        Assert.AreEqual(udpPacket2.SourcePort, tcpPacket.SourcePort);
        Assert.AreEqual(udpPacket2.DestinationPort, tcpPacket.DestinationPort);
        Assert.AreEqual(udpPacket2.Checksum, tcpPacket.Checksum);
        Assert.AreEqual(udpPacket2.Acknowledgment, tcpPacket.Acknowledgment);
        Assert.AreEqual(udpPacket2.AcknowledgmentNumber, tcpPacket.AcknowledgmentNumber);
        Assert.AreEqual(udpPacket2.SequenceNumber, tcpPacket.SequenceNumber);
        Assert.AreEqual(udpPacket2.Reset, tcpPacket.Reset);
        Assert.AreEqual(udpPacket2.Synchronize, tcpPacket.Synchronize);
        Assert.AreEqual(udpPacket2.Acknowledgment, tcpPacket.Acknowledgment);
        Assert.AreEqual(udpPacket2.Push, tcpPacket.Push);
        Assert.AreEqual(udpPacket2.WindowSize, tcpPacket.WindowSize);
        Assert.AreEqual(udpPacket2.UrgentPointer, tcpPacket.UrgentPointer);
        Assert.AreEqual(udpPacket2.DataOffset, (20 + tcpPacket.Options.Length) / 4);
        Assert.AreEqual(udpPacket2.Options.Length, tcpPacket.Options.Length);
        CollectionAssert.AreEqual(udpPacket2.Bytes, tcpPacket.Buffer.ToArray());

        // Parse
        ipPacket.Dispose();
        ipPacket = IpPacketFactory.Parse(packet2.Bytes);
        tcpPacket = ipPacket.ExtractTcp();
        Assert.AreEqual(udpPacket2.SourcePort, tcpPacket.SourcePort);
        Assert.AreEqual(udpPacket2.DestinationPort, tcpPacket.DestinationPort);
        Assert.AreEqual(udpPacket2.Checksum, tcpPacket.Checksum);
        Assert.AreEqual(udpPacket2.Acknowledgment, tcpPacket.Acknowledgment);
        Assert.AreEqual(udpPacket2.AcknowledgmentNumber, tcpPacket.AcknowledgmentNumber);
        Assert.AreEqual(udpPacket2.SequenceNumber, tcpPacket.SequenceNumber);
        Assert.AreEqual(udpPacket2.Reset, tcpPacket.Reset);
        Assert.AreEqual(udpPacket2.Synchronize, tcpPacket.Synchronize);
        Assert.AreEqual(udpPacket2.Acknowledgment, tcpPacket.Acknowledgment);
        Assert.AreEqual(udpPacket2.Push, tcpPacket.Push);
        Assert.AreEqual(udpPacket2.WindowSize, tcpPacket.WindowSize);
        Assert.AreEqual(udpPacket2.UrgentPointer, tcpPacket.UrgentPointer);
        Assert.AreEqual(udpPacket2.DataOffset, (20 + tcpPacket.Options.Length) / 4);
        Assert.AreEqual(udpPacket2.Options.Length, tcpPacket.Options.Length);
        CollectionAssert.AreEqual(udpPacket2.Bytes, tcpPacket.Buffer.ToArray());
        ipPacket.Dispose();
    }

    [TestMethod]
    [DataRow(VhIpVersion.IPv4)]
    [DataRow(VhIpVersion.IPv6)]
    public void IP_Addresses(VhIpVersion ipVersion)
    {
        var sourceAddress = GetRandomIp(ipVersion);
        var destinationAddress = GetRandomIp(ipVersion);

        // Test ip addresses change by changing VhIpPacket.SourceAddress
        var ipPacket = IpPacketFactory.BuildIp(
            sourceAddress, destinationAddress, VhIpProtocol.Raw, 0);

        Assert.AreEqual(sourceAddress, ipPacket.SourceAddress);
        Assert.AreEqual(destinationAddress, ipPacket.DestinationAddress);
        CollectionAssert.AreEqual(sourceAddress.GetAddressBytes(), ipPacket.SourceAddressSpan.ToArray());
        CollectionAssert.AreEqual(destinationAddress.GetAddressBytes(), ipPacket.DestinationAddressSpan.ToArray());
        ipPacket.Dispose();

        // Test ip addresses change by changing VhIpPacket.SourceAddressSpan 
        ipPacket = IpPacketFactory.BuildIp(GetRandomIp(ipVersion), GetRandomIp(ipVersion), VhIpProtocol.Raw, 0);
        sourceAddress.GetAddressBytes().CopyTo(ipPacket.SourceAddressSpan);
        destinationAddress.GetAddressBytes().CopyTo(ipPacket.DestinationAddressSpan);
        Assert.AreEqual(sourceAddress, ipPacket.SourceAddress);
        Assert.AreEqual(destinationAddress, ipPacket.DestinationAddress);
        CollectionAssert.AreEqual(sourceAddress.GetAddressBytes(), ipPacket.SourceAddressSpan.ToArray());
        CollectionAssert.AreEqual(destinationAddress.GetAddressBytes(), ipPacket.DestinationAddressSpan.ToArray());
        ipPacket.Dispose();
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
        var ipPacketBuffer = ipPacket.Buffer.ToArray();
        ipPacket.Dispose();

        VhLogger.Instance.LogDebug("Assert read from buffer.");
        ipPacket = new VhIpV4Packet(ipPacketBuffer);
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
        ipPacket.Dispose();
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
        var ipPacketBuffer = ipPacket.Buffer.ToArray();
        ipPacket.Dispose();

        VhLogger.Instance.LogDebug("Assert read from buffer.");
        ipPacket = new VhIpV6Packet(ipPacketBuffer);
        Assert.AreEqual(VhIpVersion.IPv6, ipPacket.Version);
        Assert.AreEqual(nextHeader, ipPacket.Protocol);
        Assert.AreEqual(nextHeader, ipPacket.NextHeader);
        Assert.AreEqual(sourceIp, ipPacket.SourceAddress);
        Assert.AreEqual(destinationIp, ipPacket.DestinationAddress);
        Assert.AreEqual(ttl, ipPacket.TimeToLive);
        Assert.AreEqual(ttl, ipPacket.HopLimit);
        ipPacket.Dispose();
    }

    [TestMethod]
    public void SimplePacket()
    {
    }
}