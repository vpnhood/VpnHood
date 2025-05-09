using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PacketDotNet;
using VpnHood.Core.Packets.VhPackets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Test.Packets;

namespace VpnHood.Test.Tests;

[TestClass]
public class PacketTest : TestBase
{
    private static IPAddress GetRandomIp(IpVersion ipVersion)
    {
        var random = new Random();
        var buffer = new byte[ipVersion == IpVersion.IPv4 ? 4 : 16];
        random.NextBytes(buffer);
        return new IPAddress(buffer);
    }

    private static IPEndPoint GetRandomEp(IpVersion ipVersion) =>
        new(GetRandomIp(ipVersion), Random.Shared.Next(0xFFFF));

    [TestMethod]
    [DataRow(IpVersion.IPv4)]
    [DataRow(IpVersion.IPv6)]
    public void IP_Addresses(IpVersion ipVersion)
    {
        var sourceAddress = GetRandomIp(ipVersion);
        var destinationAddress = GetRandomIp(ipVersion);

        // Test ip addresses change by changing VhIpPacket.SourceAddress
        var ipPacket = PacketBuilder.BuildIp(
            sourceAddress, destinationAddress, IpProtocol.Raw, 0);

        Assert.AreEqual(sourceAddress, ipPacket.SourceAddress);
        Assert.AreEqual(destinationAddress, ipPacket.DestinationAddress);
        CollectionAssert.AreEqual(sourceAddress.GetAddressBytes(), ipPacket.SourceAddressSpan.ToArray());
        CollectionAssert.AreEqual(destinationAddress.GetAddressBytes(), ipPacket.DestinationAddressSpan.ToArray());
        ipPacket.Dispose();

        // Test ip addresses change by changing VhIpPacket.SourceAddressSpan 
        ipPacket = PacketBuilder.BuildIp(GetRandomIp(ipVersion), GetRandomIp(ipVersion), IpProtocol.Raw, 0);
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

        var ipPacketNet = (IPv4Packet)NetPacketBuilder.BuildIpPacket(sourceIp, destinationIp);
        ipPacketNet.Protocol = ProtocolType.Raw;
        ipPacketNet.TimeToLive = ttl;
        ipPacketNet.Version = IPVersion.IPv4;
        ipPacketNet.FragmentFlags = fragmentFlags;
        ipPacketNet.FragmentOffset = offset;
        ipPacketNet.Id = id;
        ipPacketNet.DifferentiatedServices = dscp;
        ipPacketNet.UpdateAllChecksums();

        VhLogger.Instance.LogDebug("Assert read from buffer.");
        var ipPacket = new IpV4Packet(ipPacketNet.Bytes);
        Assert.AreEqual(IpVersion.IPv4, ipPacket.Version);
        Assert.AreEqual(sourceIp, ipPacket.SourceAddress);
        Assert.AreEqual(destinationIp, ipPacket.DestinationAddress);
        Assert.AreEqual(IpProtocol.Raw, ipPacket.Protocol);
        Assert.AreEqual(ttl, ipPacket.TimeToLive);
        Assert.AreEqual(fragmentFlags, ipPacket.FragmentFlags);
        Assert.AreEqual(offset, ipPacket.FragmentOffset);
        Assert.AreEqual(id, ipPacket.Identification);
        Assert.AreEqual(dscp, ipPacket.Dscp);
        Assert.AreEqual(ecn, ipPacket.Ecn);
        Assert.AreEqual(0, ipPacket.Payload.Length);
        CollectionAssert.AreEqual(ipPacketNet.Bytes, ipPacket.Buffer.ToArray());

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
        ipPacket = new IpV4Packet(ipPacketBuffer);
        Assert.AreEqual(IpVersion.IPv4, ipPacket.Version);
        Assert.AreEqual(IpProtocol.Raw, ipPacket.Protocol);
        Assert.AreEqual(sourceIp, ipPacket.SourceAddress);
        Assert.AreEqual(destinationIp, ipPacket.DestinationAddress);
        Assert.AreEqual(IpProtocol.Raw, ipPacket.Protocol);
        Assert.AreEqual(ttl, ipPacket.TimeToLive);
        Assert.AreEqual(fragmentFlags, ipPacket.FragmentFlags);
        Assert.AreEqual(offset, ipPacket.FragmentOffset);
        Assert.AreEqual(id, ipPacket.Identification);
        Assert.AreEqual(0, ipPacket.Payload.Length);
        CollectionAssert.AreEqual(ipPacketNet.Bytes, ipPacket.Buffer.ToArray());
        ipPacket.Dispose();
    }

    [TestMethod]
    public void IPv6()
    {
        var sourceIp = IPAddress.Parse("2001:db8::1");
        var destinationIp = IPAddress.Parse("2001:db8::2");
        byte ttl = 126;
        var flowLabel = 0x12345;
        const IpProtocol nextHeader = IpProtocol.IPv6NoNextHeader;
        byte trafficClass = 0xFF;
        var ipPacketNet = (IPv6Packet)NetPacketBuilder.BuildIpPacket(sourceIp, destinationIp);

        ipPacketNet.HopLimit = ttl;
        ipPacketNet.Version = IPVersion.IPv6;
        ipPacketNet.FlowLabel = flowLabel;
        ipPacketNet.TrafficClass = trafficClass;

        VhLogger.Instance.LogDebug("Assert read from buffer.");
        var ipPacket = new IpV6Packet(ipPacketNet.Bytes);
        Assert.AreEqual(IpVersion.IPv6, ipPacket.Version);
        Assert.AreEqual(nextHeader, ipPacket.Protocol);
        Assert.AreEqual(nextHeader, ipPacket.NextHeader);
        Assert.AreEqual(sourceIp, ipPacket.SourceAddress);
        Assert.AreEqual(destinationIp, ipPacket.DestinationAddress);
        Assert.AreEqual(ttl, ipPacket.TimeToLive);
        Assert.AreEqual(ttl, ipPacket.HopLimit);
        Assert.AreEqual(flowLabel, ipPacket.FlowLabel);
        Assert.AreEqual(trafficClass, ipPacket.TrafficClass);
        CollectionAssert.AreEqual(ipPacketNet.Bytes, ipPacket.Buffer.ToArray());


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
        ipPacket = new IpV6Packet(ipPacketBuffer);
        Assert.AreEqual(IpVersion.IPv6, ipPacket.Version);
        Assert.AreEqual(nextHeader, ipPacket.Protocol);
        Assert.AreEqual(nextHeader, ipPacket.NextHeader);
        Assert.AreEqual(sourceIp, ipPacket.SourceAddress);
        Assert.AreEqual(destinationIp, ipPacket.DestinationAddress);
        Assert.AreEqual(ttl, ipPacket.TimeToLive);
        Assert.AreEqual(ttl, ipPacket.HopLimit);
        CollectionAssert.AreEqual(ipPacketNet.Bytes, ipPacket.Buffer.ToArray());
        ipPacket.Dispose();
    }

    [TestMethod]
    [DataRow(IpVersion.IPv4)]
    [DataRow(IpVersion.IPv6)]
    public void Udp(IpVersion ipVersion)
    {
        var ipPacket = PacketBuilder.BuildUdp(
            sourceEndPoint: GetRandomEp(ipVersion),
            destinationEndPoint: GetRandomEp(ipVersion),
            payload: [0, 1, 2, 3, 4, 5]);
        var udpPacket = ipPacket.ExtractUdp();
        ipPacket.UpdateAllChecksums();

        // check with PacketDotNet
        var ipPacketNet = NetPacketBuilder.Parse(ipPacket.Buffer.ToArray());
        ipPacketNet.UpdateAllChecksums();
        var udpPacketNet = ipPacketNet.ExtractUdp();
        Assert.AreEqual(udpPacketNet.Length, udpPacket.Buffer.Length);
        Assert.AreEqual(udpPacketNet.SourcePort, udpPacket.SourcePort);
        Assert.AreEqual(udpPacketNet.DestinationPort, udpPacket.DestinationPort);
        Assert.AreEqual(udpPacketNet.Checksum, udpPacket.Checksum);
        CollectionAssert.AreEqual(udpPacketNet.Bytes, udpPacket.Buffer.ToArray());
        CollectionAssert.AreEqual(ipPacketNet.Bytes, ipPacket.Buffer.ToArray());

        // Parse
        ipPacket.Dispose();
        ipPacket = PacketBuilder.Parse(ipPacketNet.Bytes);
        udpPacket = ipPacket.ExtractUdp();
        Assert.AreEqual(udpPacketNet.Length, udpPacket.Buffer.Length);
        Assert.AreEqual(udpPacketNet.SourcePort, udpPacket.SourcePort);
        Assert.AreEqual(udpPacketNet.DestinationPort, udpPacket.DestinationPort);
        Assert.AreEqual(udpPacketNet.Checksum, udpPacket.Checksum);
        CollectionAssert.AreEqual(udpPacketNet.Bytes, udpPacket.Buffer.ToArray());
        CollectionAssert.AreEqual(ipPacketNet.Bytes, ipPacket.Buffer.ToArray());
        ipPacket.Dispose();
    }

    [TestMethod]
    [DataRow(IpVersion.IPv4, true)]
    [DataRow(IpVersion.IPv4, false)]
    [DataRow(IpVersion.IPv6, true)]
    public void Tcp(IpVersion ipVersion, bool mode)
    {
        var ipPacket = PacketBuilder.BuildTcp(
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
        var ipPacketNet = NetPacketBuilder.Parse(ipPacket.Buffer.ToArray());
        ipPacketNet.UpdateAllChecksums();
        var tcpPacketNet = ipPacketNet.ExtractTcp();
        Assert.AreEqual(tcpPacketNet.SourcePort, tcpPacket.SourcePort);
        Assert.AreEqual(tcpPacketNet.DestinationPort, tcpPacket.DestinationPort);
        Assert.AreEqual(tcpPacketNet.Checksum, tcpPacket.Checksum);
        Assert.AreEqual(tcpPacketNet.Acknowledgment, tcpPacket.Acknowledgment);
        Assert.AreEqual(tcpPacketNet.AcknowledgmentNumber, tcpPacket.AcknowledgmentNumber);
        Assert.AreEqual(tcpPacketNet.SequenceNumber, tcpPacket.SequenceNumber);
        Assert.AreEqual(tcpPacketNet.Reset, tcpPacket.Reset);
        Assert.AreEqual(tcpPacketNet.Synchronize, tcpPacket.Synchronize);
        Assert.AreEqual(tcpPacketNet.Acknowledgment, tcpPacket.Acknowledgment);
        Assert.AreEqual(tcpPacketNet.Push, tcpPacket.Push);
        Assert.AreEqual(tcpPacketNet.WindowSize, tcpPacket.WindowSize);
        Assert.AreEqual(tcpPacketNet.UrgentPointer, tcpPacket.UrgentPointer);
        Assert.AreEqual(tcpPacketNet.DataOffset, (20 + tcpPacket.Options.Length) / 4);
        Assert.AreEqual(tcpPacketNet.Options.Length, tcpPacket.Options.Length);
        CollectionAssert.AreEqual(tcpPacketNet.Bytes, tcpPacket.Buffer.ToArray());
        CollectionAssert.AreEqual(ipPacketNet.Bytes, ipPacket.Buffer.ToArray());

        // Parse
        ipPacket.Dispose();
        ipPacket = PacketBuilder.Parse(ipPacketNet.Bytes);
        tcpPacket = ipPacket.ExtractTcp();
        Assert.AreEqual(tcpPacketNet.SourcePort, tcpPacket.SourcePort);
        Assert.AreEqual(tcpPacketNet.DestinationPort, tcpPacket.DestinationPort);
        Assert.AreEqual(tcpPacketNet.Checksum, tcpPacket.Checksum);
        Assert.AreEqual(tcpPacketNet.Acknowledgment, tcpPacket.Acknowledgment);
        Assert.AreEqual(tcpPacketNet.AcknowledgmentNumber, tcpPacket.AcknowledgmentNumber);
        Assert.AreEqual(tcpPacketNet.SequenceNumber, tcpPacket.SequenceNumber);
        Assert.AreEqual(tcpPacketNet.Reset, tcpPacket.Reset);
        Assert.AreEqual(tcpPacketNet.Synchronize, tcpPacket.Synchronize);
        Assert.AreEqual(tcpPacketNet.Acknowledgment, tcpPacket.Acknowledgment);
        Assert.AreEqual(tcpPacketNet.Push, tcpPacket.Push);
        Assert.AreEqual(tcpPacketNet.WindowSize, tcpPacket.WindowSize);
        Assert.AreEqual(tcpPacketNet.UrgentPointer, tcpPacket.UrgentPointer);
        Assert.AreEqual(tcpPacketNet.DataOffset, (20 + tcpPacket.Options.Length) / 4);
        Assert.AreEqual(tcpPacketNet.Options.Length, tcpPacket.Options.Length);
        CollectionAssert.AreEqual(tcpPacketNet.Bytes, tcpPacket.Buffer.ToArray());
        CollectionAssert.AreEqual(ipPacketNet.Bytes, ipPacket.Buffer.ToArray());
        ipPacket.Dispose();
    }

    [TestMethod]
    public void IcmpV4()
    {
        var ipPacket = PacketBuilder.BuildIcmpV4EchoRequest(
            sourceAddress: GetRandomIp(IpVersion.IPv4),
            destinationAddress: GetRandomIp(IpVersion.IPv4),
            payload: [0, 1, 2, 3, 4, 5]);
        var icmpPacket = ipPacket.ExtractIcmpV4();
        icmpPacket.Checksum = 0x1234;
        ipPacket.UpdateAllChecksums();

        // check with PacketDotNet
        var ipPacketNet = NetPacketBuilder.Parse(ipPacket.Buffer.ToArray());
        ipPacketNet.UpdateAllChecksums();
        var icmpPacketNet = ipPacketNet.ExtractIcmpV4();
        Assert.AreEqual((byte)((ushort)icmpPacketNet.TypeCode >> 8), (byte)icmpPacket.Type);
        Assert.AreEqual((byte)((ushort)icmpPacketNet.TypeCode & 0xFF), icmpPacket.Code);
        Assert.AreEqual(icmpPacketNet.Id, icmpPacket.Identifier);
        Assert.AreEqual(icmpPacketNet.Sequence, icmpPacket.SequenceNumber);
        Assert.AreEqual(icmpPacketNet.Checksum, icmpPacket.Checksum);
        CollectionAssert.AreEqual(icmpPacketNet.Bytes, icmpPacket.Buffer.ToArray());
        CollectionAssert.AreEqual(ipPacketNet.Bytes, ipPacket.Buffer.ToArray());

        // Parse
        ipPacket.Dispose();
        ipPacket = PacketBuilder.Parse(ipPacketNet.Bytes);
        icmpPacket = ipPacket.ExtractIcmpV4();
        Assert.AreEqual((byte)((ushort)icmpPacketNet.TypeCode >> 8), (byte)icmpPacket.Type);
        Assert.AreEqual((byte)((ushort)icmpPacketNet.TypeCode & 0xFF), (int)icmpPacket.Code);
        Assert.AreEqual(icmpPacketNet.Id, icmpPacket.Identifier);
        Assert.AreEqual(icmpPacketNet.Sequence, icmpPacket.SequenceNumber);
        Assert.AreEqual(icmpPacketNet.Checksum, icmpPacket.Checksum);
        CollectionAssert.AreEqual(icmpPacketNet.Bytes, icmpPacket.Buffer.ToArray());
        CollectionAssert.AreEqual(ipPacketNet.Bytes, ipPacket.Buffer.ToArray());
        ipPacket.Dispose();
    }


    [TestMethod]
    public void IcmpV6()
    {
        var ipPacket = PacketBuilder.BuildIcmpV6EchoRequest(
            sourceAddress: GetRandomIp(IpVersion.IPv6),
            destinationAddress: GetRandomIp(IpVersion.IPv6),
            payload: [0, 1, 2, 3, 4, 5]);
        var icmpPacket = ipPacket.ExtractIcmpV6();
        icmpPacket.Checksum = 0x1234;
        ipPacket.UpdateAllChecksums();

        // check with PacketDotNet
        var ipPacketNet = NetPacketBuilder.Parse(ipPacket.Buffer.ToArray());
        ipPacketNet.UpdateAllChecksums();
        var icmpPacketNet = ipPacketNet.ExtractIcmpV6();
        Assert.AreEqual((int)icmpPacketNet.Type, (int)icmpPacket.Type);
        Assert.AreEqual(icmpPacketNet.Code, icmpPacket.Code);
        Assert.AreEqual(icmpPacketNet.Checksum, icmpPacket.Checksum);
        CollectionAssert.AreEqual(icmpPacketNet.Bytes, icmpPacket.Buffer.ToArray());
        CollectionAssert.AreEqual(ipPacketNet.Bytes, ipPacket.Buffer.ToArray());

        // Parse
        ipPacket.Dispose();
        ipPacket = PacketBuilder.Parse(ipPacketNet.Bytes);
        icmpPacket = ipPacket.ExtractIcmpV6();
        Assert.AreEqual((int)icmpPacketNet.Type, (int)icmpPacket.Type);
        Assert.AreEqual(icmpPacketNet.Code, icmpPacket.Code);
        Assert.AreEqual(icmpPacketNet.Checksum, icmpPacket.Checksum);
        CollectionAssert.AreEqual(icmpPacketNet.Bytes, icmpPacket.Buffer.ToArray());
        CollectionAssert.AreEqual(ipPacketNet.Bytes, ipPacket.Buffer.ToArray());
        ipPacket.Dispose();
    }


    [TestMethod]
    [DataRow(IpVersion.IPv4)]
    [DataRow(IpVersion.IPv6)]
    public void IcmpPacketTooBig(IpVersion ipVersion)
    {
        var orgPacket = PacketBuilder.BuildUdp(
            GetRandomEp(ipVersion), GetRandomEp(ipVersion), []);

        var mtu = 0x0102;
        var ipPacket = PacketBuilder.BuildIcmpPacketTooBigReply(orgPacket, mtu, false);
        var ipPacketNet = NetPacketBuilder.BuildIcmpPacketTooBigReply(
            NetPacketBuilder.Parse(orgPacket.Buffer.ToArray()), mtu, false);

        var b1 = ipPacket.Buffer.ToArray();
        var b2 = ipPacketNet.Bytes;
        CollectionAssert.AreEqual(b1, b2);

        ipPacket.UpdateAllChecksums();
        ipPacketNet.UpdateAllChecksums();
        CollectionAssert.AreEqual(ipPacketNet.Bytes, ipPacket.Buffer.ToArray());
    }
}