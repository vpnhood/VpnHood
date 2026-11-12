using System.Net;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Logging;
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
        ipPacket.SourceAddressSpan = sourceAddress.GetAddressBytes().AsSpan();
        ipPacket.DestinationAddressSpan = destinationAddress.GetAddressBytes().AsSpan();
        Assert.AreEqual(sourceAddress, ipPacket.SourceAddress);
        Assert.AreEqual(destinationAddress, ipPacket.DestinationAddress);
        CollectionAssert.AreEqual(sourceAddress.GetAddressBytes(), ipPacket.SourceAddressSpan.ToArray());
        CollectionAssert.AreEqual(destinationAddress.GetAddressBytes(), ipPacket.DestinationAddressSpan.ToArray());
        ipPacket.Dispose();
    }

    [TestMethod]
    [DataRow(IpVersion.IPv4)]
    [DataRow(IpVersion.IPv6)]
    public void IP_Address_values(IpVersion ipVersion)
    {
        var sourceAddress = GetRandomIp(ipVersion);
        var destinationAddress = GetRandomIp(ipVersion);
        using var ipPacket = PacketBuilder.BuildIp(
            sourceAddress.ToValue(), destinationAddress.ToValue(), IpProtocol.Raw, 0);
        Assert.AreEqual(sourceAddress.ToValue(), ipPacket.SourceAddressValue);
        Assert.AreEqual(destinationAddress.ToValue(), ipPacket.DestinationAddressValue);
        Assert.AreEqual(sourceAddress, ipPacket.SourceAddress);
        Assert.AreEqual(destinationAddress, ipPacket.DestinationAddress);

        // a set writes the header and drops the IPAddress the packet kept
        var keptSource = ipPacket.SourceAddress;
        var keptDestination = ipPacket.DestinationAddress;
        sourceAddress = GetRandomIp(ipVersion);
        destinationAddress = GetRandomIp(ipVersion);
        ipPacket.SourceAddressValue = sourceAddress.ToValue();
        ipPacket.DestinationAddressValue = destinationAddress.ToValue();
        CollectionAssert.AreEqual(sourceAddress.GetAddressBytes(), ipPacket.SourceAddressSpan.ToArray());
        CollectionAssert.AreEqual(destinationAddress.GetAddressBytes(), ipPacket.DestinationAddressSpan.ToArray());
        Assert.AreNotSame(keptSource, ipPacket.SourceAddress);
        Assert.AreNotSame(keptDestination, ipPacket.DestinationAddress);
        Assert.AreEqual(sourceAddress, ipPacket.SourceAddress);
        Assert.AreEqual(destinationAddress, ipPacket.DestinationAddress);

        // a header has no room for a scope id: the packet keeps it with the address, as its IPAddress setter does
        if (ipVersion == IpVersion.IPv6) {
            var scoped = IPAddress.Parse("fe80::1%3");
            ipPacket.DestinationAddressValue = scoped.ToValue();
            CollectionAssert.AreEqual(IPAddress.Parse("fe80::1").GetAddressBytes(),
                ipPacket.DestinationAddressSpan.ToArray());
            Assert.AreEqual(scoped, ipPacket.DestinationAddress);
            Assert.AreEqual(scoped.ToValue(), ipPacket.DestinationAddressValue);

            // an address without a scope drops the scoped one
            ipPacket.DestinationAddressValue = IPAddress.Parse("fe80::1").ToValue();
            Assert.AreEqual(IPAddress.Parse("fe80::1"), ipPacket.DestinationAddress);
        }
    }

    [TestMethod]
    public void Endpoints_keep_their_scope_id()
    {
        var source = new IpEndPointValue(IPAddress.Parse("fe80::2%3"), 5353);
        var destination = new IpEndPointValue(IPAddress.Parse("fe80::1%3"), 53);

        // set on a packet, read back as an endpoint and as the IPAddress a socket would take
        using var udpPacket = PacketBuilder.BuildUdp(IPAddress.Parse("fe80::9"), IPAddress.Parse("fe80::8"), 1, 2,
            new byte[8]);
        udpPacket.SetSourceEndPoint(source);
        udpPacket.SetDestinationEndPoint(destination);
        Assert.AreEqual(source, udpPacket.GetSourceEndPoint());
        Assert.AreEqual(destination, udpPacket.GetDestinationEndPoint());
        Assert.AreEqual(3L, udpPacket.DestinationAddress.ScopeId);

        // built from them, as the TCP stack builds a segment
        using var tcpPacket = PacketBuilder.BuildTcp(source, destination, [], []);
        Assert.AreEqual(source, tcpPacket.GetSourceEndPoint());
        Assert.AreEqual(destination, tcpPacket.GetDestinationEndPoint());
        Assert.AreEqual(3L, tcpPacket.SourceAddress.ScopeId);
    }

    [TestMethod]
    [DataRow(IpVersion.IPv4)]
    [DataRow(IpVersion.IPv6)]
    public void IP_Address_of_the_other_version_changes_nothing(IpVersion ipVersion)
    {
        using var ipPacket = PacketBuilder.BuildIp(GetRandomIp(ipVersion), GetRandomIp(ipVersion), IpProtocol.Raw, 0);
        var keptSource = ipPacket.SourceAddress;
        var keptDestination = ipPacket.DestinationAddress;
        var header = ipPacket.Header.ToArray();
        var other = GetRandomIp(ipVersion == IpVersion.IPv4 ? IpVersion.IPv6 : IpVersion.IPv4);

        Assert.ThrowsExactly<ArgumentException>(() => ipPacket.SourceAddress = other);
        Assert.ThrowsExactly<ArgumentException>(() => ipPacket.DestinationAddress = other);
        Assert.ThrowsExactly<ArgumentException>(() => ipPacket.SourceAddressValue = other.ToValue());
        Assert.ThrowsExactly<ArgumentException>(() => ipPacket.DestinationAddressValue = other.ToValue());
        Assert.ThrowsExactly<ArgumentException>(() => ipPacket.DestinationAddressValue = default);
        Assert.ThrowsExactly<ArgumentException>(() => { ipPacket.SourceAddressSpan = other.GetAddressBytes(); });
        Assert.ThrowsExactly<ArgumentException>(() => { ipPacket.DestinationAddressSpan = other.GetAddressBytes(); });
        Assert.ThrowsExactly<ArgumentException>(() =>
            ipPacket.SetDestinationEndPoint(new IpEndPointValue(other, 0)));

        // a span of no address's length, which a shorter one would have written over the start of the address
        foreach (var length in new[] { 0, 3, 5, 15, 17 }) {
            Assert.ThrowsExactly<ArgumentException>(() => { ipPacket.SourceAddressSpan = new byte[length]; });
            Assert.ThrowsExactly<ArgumentException>(() => { ipPacket.DestinationAddressSpan = new byte[length]; });
        }

        CollectionAssert.AreEqual(header, ipPacket.Header.ToArray());
        Assert.AreSame(keptSource, ipPacket.SourceAddress);
        Assert.AreSame(keptDestination, ipPacket.DestinationAddress);
    }

    [TestMethod]
    public void Endpoint_with_a_port_changes_nothing_where_the_protocol_has_no_ports()
    {
        var endPoint = new IpEndPointValue(IPAddress.Parse("10.0.0.9"), 53);
        using var ipPacket = PacketBuilder.BuildIcmpV4EchoRequest(IPAddress.Parse("10.0.0.2"),
            IPAddress.Parse("10.0.0.3"), [1, 2, 3]);
        var bytes = ipPacket.Buffer.ToArray();

        Assert.ThrowsExactly<InvalidOperationException>(() => ipPacket.SetSourceEndPoint(endPoint));
        Assert.ThrowsExactly<InvalidOperationException>(() => ipPacket.SetDestinationEndPoint(endPoint));
        CollectionAssert.AreEqual(bytes, ipPacket.Buffer.ToArray());

        // without a port, the address alone is set
        ipPacket.SetSourceEndPoint(new IpEndPointValue(endPoint.AddressValue, 0));
        Assert.AreEqual(endPoint.AddressValue, ipPacket.SourceAddressValue);
    }

    [TestMethod]
    public void Endpoint_changes_nothing_on_a_fragment()
    {
        // a fragment holds only a piece of the datagram its ports are in
        var endPoint = new IpEndPointValue(IPAddress.Parse("10.0.0.9"), 53);
        using var udpPacket = (IpV4Packet)PacketBuilder.BuildUdp(IPAddress.Parse("10.0.0.2"),
            IPAddress.Parse("10.0.0.3"), 1000, 53, [1, 2, 3]);
        udpPacket.MoreFragments = true;
        using var ipPacket = PacketBuilder.Parse(udpPacket.Buffer.Span);
        var bytes = ipPacket.Buffer.ToArray();

        Assert.ThrowsExactly<InvalidDataException>(() => ipPacket.SetSourceEndPoint(endPoint));
        Assert.ThrowsExactly<InvalidDataException>(() => ipPacket.SetDestinationEndPoint(endPoint));
        CollectionAssert.AreEqual(bytes, ipPacket.Buffer.ToArray());
    }

    [TestMethod]
    public void Disposed_packet_is_named_without_its_buffer()
    {
        // error paths log a packet another owner has disposed
        var ipPacket = PacketBuilder.BuildUdp(IPAddress.Parse("10.0.0.2"), IPAddress.Parse("10.0.0.3"), 1, 53,
            new byte[16]);
        ipPacket.Dispose();

        Assert.AreEqual("Disposed, TotalLength:44", ipPacket.ToString());
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
        ipPacket.UpdateAllChecksums();
        Assert.AreEqual(ipPacketNet.Checksum, ipPacket.HeaderChecksum);

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
    public void IPv6_build_refuses_a_packet_longer_than_its_payload_length_field_holds()
    {
        // 40 + 65535 bytes: the longest a payload length field can say, which a parse reads back
        using var longest = new IpV6Packet(new byte[40 + 0xFFFF], 40 + 0xFFFF, IpProtocol.Udp);
        using var parsed = PacketBuilder.Attach(longest.Buffer);
        Assert.AreEqual(40 + 0xFFFF, parsed.PacketLength);

        Assert.ThrowsExactly<ArgumentException>(() =>
            new IpV6Packet(new byte[40 + 0x10000], 40 + 0x10000, IpProtocol.Udp));
    }

    [TestMethod]
    public void IPv4_build_refuses_options_longer_than_the_packet()
    {
        // 40 bytes of options in a 20-byte packet: its header would say it is 60 bytes long
        Assert.ThrowsExactly<ArgumentException>(() => new IpV4Packet(new byte[20], 20, IpProtocol.Udp, 40));
        Assert.ThrowsExactly<ArgumentException>(() => new IpV4Packet(new byte[60], 59, IpProtocol.Udp, 40));

        // a packet that is its header alone
        using var ipPacket = new IpV4Packet(new byte[60], 60, IpProtocol.Udp, 40);
        Assert.AreEqual(60, ipPacket.Header.Length);
        Assert.AreEqual(40, ipPacket.Options.Length);
        Assert.AreEqual(0, ipPacket.Payload.Length);
    }

    [TestMethod]
    public void A_refused_build_leaves_the_memory_as_it_was()
    {
        var bytes = new byte[64];
        Array.Fill(bytes, (byte)0xAA);
        var bytesBefore = bytes.ToArray();

        // IPv4 options of a length no header has
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new IpV4Packet(bytes, 64, IpProtocol.Udp, 3));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new IpV4Packet(bytes, 64, IpProtocol.Udp, 44));
        CollectionAssert.AreEqual(bytesBefore, bytes);

        // a packet shorter than its header
        Assert.ThrowsExactly<ArgumentException>(() => new IpV4Packet(bytes, 24, IpProtocol.Udp, 8));
        Assert.ThrowsExactly<ArgumentException>(() => new IpV4Packet(bytes, 10, IpProtocol.Udp, 0));
        Assert.ThrowsExactly<ArgumentException>(() => new IpV6Packet(bytes, 30, IpProtocol.Udp));
        CollectionAssert.AreEqual(bytesBefore, bytes);
    }

    [TestMethod]
    public void A_version_that_is_neither_4_nor_6_is_not_supported()
    {
        byte[] bytes = [0x50, 0, 0, 20, .. new byte[16]];

        Assert.ThrowsExactly<NotSupportedException>(() => PacketUtil.ReadPacketLength(bytes));
        Assert.ThrowsExactly<NotSupportedException>(() => PacketBuilder.Parse(bytes));
        Assert.ThrowsExactly<NotSupportedException>(() => PacketBuilder.Attach(bytes));
    }

    [TestMethod]
    public void IPv6_reads_through_extension_headers()
    {
        using var tcpPacket = PacketBuilder.BuildTcp(IPEndPoint.Parse("[2001:db8::1]:1000"),
            IPEndPoint.Parse("[2001:db8::2]:443"), [], [1, 2, 3]);
        tcpPacket.UpdateAllChecksums();

        // an atomic fragment, a Fragment header with no offset nor more fragments, is a whole packet
        using var ipPacket = IpV6ExtensionHeaderBuilder.Insert(tcpPacket,
            IpV6ExtensionHeaderBuilder.Options(IpProtocol.IPv6HopByHopOptions),
            IpV6ExtensionHeaderBuilder.Authentication(),
            IpV6ExtensionHeaderBuilder.Fragment(offset: 0, moreFragments: false),
            IpV6ExtensionHeaderBuilder.Options(IpProtocol.IPv6DestinationOptions));

        Assert.AreEqual(IpProtocol.IPv6HopByHopOptions, ((IpV6Packet)ipPacket).NextHeader);
        Assert.AreEqual(IpProtocol.Tcp, ipPacket.Protocol);
        Assert.IsFalse(ipPacket.IsFragment);
        Assert.AreEqual(40 + 8 + 12 + 8 + 8, ipPacket.Header.Length);
        Assert.AreEqual(443, ipPacket.GetDestinationEndPoint().Port);

        // the TCP checksum covers the TCP packet alone, as without the headers
        Assert.IsTrue(ipPacket.ExtractTcp().IsChecksumValid(ipPacket));
    }

    [TestMethod]
    public void IPv6_stops_at_unread_extension_header()
    {
        using var tcpPacket = PacketBuilder.BuildTcp(IPEndPoint.Parse("[2001:db8::1]:1000"),
            IPEndPoint.Parse("[2001:db8::2]:443"), [], [1, 2, 3]);

        // a Routing header, whose final destination the TCP checksum would cover
        using var routedPacket = IpV6ExtensionHeaderBuilder.Insert(tcpPacket,
            IpV6ExtensionHeaderBuilder.Options(IpProtocol.IPv6HopByHopOptions),
            IpV6ExtensionHeaderBuilder.Routing());
        Assert.AreEqual(IpProtocol.IPv6RoutingHeader, routedPacket.Protocol);
        Assert.AreEqual(48, routedPacket.Header.Length);

        // a header that runs past the packet
        using var truncatedPacket = IpV6ExtensionHeaderBuilder.Insert(tcpPacket,
            IpV6ExtensionHeaderBuilder.Options(IpProtocol.IPv6DestinationOptions, length: 10));
        Assert.AreEqual(IpProtocol.IPv6DestinationOptions, truncatedPacket.Protocol);
        Assert.AreEqual(40, truncatedPacket.Header.Length);
    }

    [TestMethod]
    public void IPv6_fragment()
    {
        using var udpPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("[2001:db8::1]:1000"),
            IPEndPoint.Parse("[2001:db8::2]:53"), [1, 2, 3]);

        // the first piece holds the upper layer's header, not its whole packet
        using var firstPiece = IpV6ExtensionHeaderBuilder.Insert(udpPacket,
            IpV6ExtensionHeaderBuilder.Fragment(offset: 0, moreFragments: true));
        Assert.AreEqual(IpProtocol.Udp, firstPiece.Protocol);
        Assert.IsTrue(firstPiece.IsFragment);
        Assert.AreEqual(48, firstPiece.Header.Length);
        Assert.ThrowsExactly<InvalidDataException>(() => firstPiece.ExtractUdp());

        // a later piece holds no upper-layer header at all
        using var laterPiece = IpV6ExtensionHeaderBuilder.Insert(udpPacket,
            IpV6ExtensionHeaderBuilder.Fragment(offset: 1, moreFragments: false));
        Assert.AreEqual(IpProtocol.Udp, laterPiece.Protocol);
        Assert.IsTrue(laterPiece.IsFragment);
        Assert.AreEqual(48, laterPiece.Header.Length);
    }

    [TestMethod]
    public void IPv4_fragment()
    {
        using var echoPacket = (IpV4Packet)PacketBuilder.BuildIcmpV4EchoRequest(IPAddress.Parse("10.0.0.2"),
            IPAddress.Parse("10.0.0.3"), [1, 2, 3]);
        Assert.IsFalse(echoPacket.IsFragment);
        echoPacket.MoreFragments = true;

        using var ipPacket = (IpV4Packet)PacketBuilder.Parse(echoPacket.Buffer.Span);
        Assert.IsTrue(ipPacket.IsFragment);
        Assert.IsFalse(ipPacket.IsIcmpEcho());
        Assert.ThrowsExactly<InvalidDataException>(() => ipPacket.ExtractIcmpV4());

        // its header's checksum only, as the ICMP one covers the whole datagram
        var payload = ipPacket.Payload.ToArray();
        ipPacket.UpdateAllChecksums();
        Assert.IsTrue(ipPacket.IsHeaderChecksumValid());
        CollectionAssert.AreEqual(payload, ipPacket.Payload.ToArray());

        // an offset makes a fragment too
        ipPacket.MoreFragments = false;
        ipPacket.FragmentOffset = 1;
        Assert.IsTrue(ipPacket.IsFragment);
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
        var udpPacketNet = ipPacketNet.ExtractUdp();
        Assert.AreEqual(udpPacketNet.Checksum, udpPacket.Checksum);

        ipPacketNet.UpdateAllChecksums();
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
    public void Udp_build_refuses_a_datagram_longer_than_its_length_field_holds()
    {
        // 65535 bytes: the longest a length field can say, which a parse reads back
        var longest = new VpnHood.Net.Packets.UdpPacket(new byte[0xFFFF], building: true);
        var parsed = new VpnHood.Net.Packets.UdpPacket(longest.Buffer, building: false);
        Assert.AreEqual(0xFFFF, parsed.Buffer.Length);

        Assert.ThrowsExactly<ArgumentException>(() =>
            new VpnHood.Net.Packets.UdpPacket(new byte[0x10000], building: true));
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
        var tcpPacketNet = ipPacketNet.ExtractTcp();
        Assert.AreEqual(tcpPacketNet.Checksum, tcpPacket.Checksum);

        ipPacketNet.UpdateAllChecksums();
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
    public void Tcp_build_names_a_buffer_short_of_its_options()
    {
        using var ipPacket = PacketBuilder.BuildIp(GetRandomIp(IpVersion.IPv4), GetRandomIp(IpVersion.IPv4),
            IpProtocol.Tcp, payloadLength: 20);
        var ex = Assert.ThrowsExactly<ArgumentException>(() => ipPacket.BuildTcp(optionsLength: 4));
        Assert.AreEqual("buffer", ex.ParamName);
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
        var icmpPacketNet = ipPacketNet.ExtractIcmpV4();
        Assert.AreEqual(icmpPacketNet.Checksum, icmpPacket.Checksum);

        ipPacketNet.UpdateAllChecksums();
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
        var icmpPacketNet = ipPacketNet.ExtractIcmpV6();
        Assert.AreEqual(icmpPacketNet.Checksum, icmpPacket.Checksum);

        ipPacketNet.UpdateAllChecksums();
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

    [TestMethod]
    public void IcmpPacketTooBig_carries_an_IPv6_mtu_above_65535()
    {
        using var orgPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("[2001:db8::1]:1000"),
            IPEndPoint.Parse("[2001:db8::2]:53"), []);

        // ICMPv6's MTU field is 32 bits (RFC 4443 3.2)
        using var ipPacket = PacketBuilder.BuildIcmpPacketTooBigReply(orgPacket, 70_000);
        Assert.AreEqual(70_000u, ipPacket.ExtractIcmpV6().MessageSpecific);
    }

    [TestMethod]
    public void IcmpV6_error_is_no_longer_than_the_minimum_mtu()
    {
        var payload = new byte[1400];
        new Random(4).NextBytes(payload);
        using var orgPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("[2001:db8::1]:1000"),
            IPEndPoint.Parse("[2001:db8::2]:53"), payload);

        // as much of the packet as an error of 1280 bytes holds (RFC 4443 2.4 c)
        using var ipPacket = PacketBuilder.BuildIcmpUnreachableReply(orgPacket);
        Assert.AreEqual(1280, ipPacket.PacketLength);
        CollectionAssert.AreEqual(orgPacket.Buffer[..(1280 - 40 - 8)].ToArray(),
            ipPacket.ExtractIcmpV6().Payload.ToArray());
        Assert.IsTrue(ipPacket.ExtractIcmpV6().IsChecksumValid(ipPacket));

        // a packet that fits goes whole
        using var shortPacket = PacketBuilder.BuildUdp(IPEndPoint.Parse("[2001:db8::1]:1000"),
            IPEndPoint.Parse("[2001:db8::2]:53"), [1, 2, 3]);
        using var shortReply = PacketBuilder.BuildIcmpUnreachableReply(shortPacket);
        CollectionAssert.AreEqual(shortPacket.Buffer.ToArray(), shortReply.ExtractIcmpV6().Payload.ToArray());
    }
}