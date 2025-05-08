using System.Buffers;
using System.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Packets.VhPackets;

public static class IpPacketBuilder
{
    public static VhIpPacket Parse(Memory<byte> buffer)
    {
        if (buffer.Length < 1)
            throw new ArgumentException("Buffer too small to determine IP version.", nameof(buffer));

        var version = buffer.Span[0] >> 4;
        return version switch {
            4 => new VhIpV4Packet(buffer),
            6 => new VhIpV6Packet(buffer),
            _ => throw new NotSupportedException($"IP version {version} not supported."),
        };
    }

    public static VhIpPacket Parse(IMemoryOwner<byte> memoryOwner, int packetLength)
    {
        if (memoryOwner.Memory.Length < 1)
            throw new ArgumentException("Buffer too small to determine IP version.", nameof(memoryOwner));

        var version = memoryOwner.Memory.Span[0] >> 4;
        return version switch {
            4 => new VhIpV4Packet(memoryOwner, packetLength),
            6 => new VhIpV6Packet(memoryOwner, packetLength),
            _ => throw new NotSupportedException($"IP version {version} not supported."),
        };
    }

    public static VhIpPacket BuildIp(IPAddress sourceAddress, IPAddress destinationAddress,
        VhIpProtocol protocol, int payloadLength)
    {
        return BuildIp(
            sourceAddress.GetAddressBytes(), destinationAddress.GetAddressBytes(),
            protocol, payloadLength);
    }

    public static VhIpPacket BuildIp(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress,
        VhIpProtocol protocol, int payloadLength, MemoryPool<byte>? memoryPool = null)
    {
        if (sourceAddress.Length != destinationAddress.Length)
            throw new ArgumentException("SourceAddress and DestinationAddress must have a same ip version.");

        memoryPool ??= MemoryPool<byte>.Shared;

        VhIpPacket ipPacket = sourceAddress.Length switch {
            4 when destinationAddress.Length == 4 =>
                new VhIpV4Packet(memoryPool.Rent(20 + 8 + payloadLength), 20 + 8 + payloadLength, protocol, 0),
            16 when destinationAddress.Length == 16 =>
                new VhIpV6Packet(memoryPool.Rent(40 + 8 + payloadLength), 40 + 8 + payloadLength, protocol),
            _ => throw new NotSupportedException($"IP version {sourceAddress.Length} not supported.")
        };

        sourceAddress.CopyTo(ipPacket.SourceAddressSpan);
        destinationAddress.CopyTo(ipPacket.DestinationAddressSpan);
        ipPacket.TimeToLive = 64;
        return ipPacket;
    }

    public static VhIpPacket BuildUdp(IPEndPoint sourceEndPoint, IPEndPoint destinationEndPoint,
        ReadOnlySpan<byte> payload)
    {
        return BuildUdp(
            sourceEndPoint.Address.GetAddressBytes(), destinationEndPoint.Address.GetAddressBytes(),
            sourceEndPoint.Port, destinationEndPoint.Port,
            payload);
    }


    public static VhIpPacket BuildUdp(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress,
        int sourcePort, int destinationPort, ReadOnlySpan<byte> payload)
    {
        var ipPacket = BuildIp(sourceAddress, destinationAddress, VhIpProtocol.Udp, 8 + payload.Length);
        var udpPacket = ipPacket.BuildUdp();
        udpPacket.SourcePort = (ushort)sourcePort;
        udpPacket.DestinationPort = (ushort)destinationPort;
        payload.CopyTo(udpPacket.Payload.Span);
        return ipPacket;
    }

    public static VhIpPacket BuildTcp(IPEndPoint sourceEndPoint, IPEndPoint destinationEndPoint,
        ReadOnlySpan<byte> options, ReadOnlySpan<byte> payload)
    {
        return BuildTcp(
            sourceEndPoint.Address.GetAddressBytes(), destinationEndPoint.Address.GetAddressBytes(),
            sourceEndPoint.Port, destinationEndPoint.Port,
            options, payload);
    }

    public static VhIpPacket BuildTcp(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress,
        int sourcePort, int destinationPort, ReadOnlySpan<byte> options, ReadOnlySpan<byte> payload)
    {
        var ipPacket = BuildIp(sourceAddress, destinationAddress, VhIpProtocol.Tcp, 20 + options.Length + payload.Length);
        var udpPacket = ipPacket.BuildTcp();
        udpPacket.SourcePort = (ushort)sourcePort;
        udpPacket.DestinationPort = (ushort)destinationPort;
        payload.CopyTo(udpPacket.Payload.Span);
        options.CopyTo(udpPacket.Options.Span);
        return ipPacket;
    }

    public static VhIpPacket BuildIcmpV4(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress,
        ReadOnlySpan<byte> payload)
    {
        var ipPacket = BuildIp(sourceAddress, destinationAddress, VhIpProtocol.IcmpV4, 8 + payload.Length);
        payload.CopyTo(ipPacket.BuildIcmpV4().Payload.Span);
        return ipPacket;
    }
    public static VhIpPacket BuildIcmpV6(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress,
        ReadOnlySpan<byte> payload)
    {
        var ipPacket = BuildIp(sourceAddress, destinationAddress, VhIpProtocol.IcmpV6, 8 + payload.Length);
        payload.CopyTo(ipPacket.BuildIcmpV6().Payload.Span);
        return ipPacket;
    }

    public static VhIpPacket BuildIcmpEchoRequest(IPAddress sourceAddress, IPAddress destinationAddress,
        byte[] payload, ushort identifier = 0, ushort sequenceNumber = 0, bool calculateChecksum = true)
    {
        return BuildIcmpEchoRequest(
            sourceAddress.GetAddressBytes(), destinationAddress.GetAddressBytes(),
            payload, identifier, sequenceNumber, calculateChecksum);
    }

    public static VhIpPacket BuildIcmpEchoRequest(ReadOnlySpan<byte> sourceAddress,
        ReadOnlySpan<byte> destinationAddress,
        byte[] payload, ushort identifier = 0, ushort sequenceNumber = 0, bool calculateChecksum = true)
    {
        if (sourceAddress.Length != destinationAddress.Length)
            throw new ArgumentException("SourceAddress and DestinationAddress must have a same ip version.");

        return sourceAddress.Length switch {
            4 => BuildIcmpEchoRequestV4(sourceAddress, destinationAddress, payload, identifier, sequenceNumber, calculateChecksum),
            16 => BuildIcmpEchoRequestV6(sourceAddress, destinationAddress, payload, identifier, sequenceNumber, calculateChecksum),
            _ => throw new NotSupportedException($"IP version {sourceAddress.Length} not supported.")
        };
    }

    public static VhIpPacket BuildIcmpEchoRequestV4(IPAddress sourceAddress, IPAddress destinationAddress,
        byte[] payload, ushort identifier = 0, ushort sequenceNumber = 0, bool calculateChecksum = true)
    {
        return BuildIcmpEchoRequestV4(
            sourceAddress.GetAddressBytes(), destinationAddress.GetAddressBytes(),
            payload, identifier, sequenceNumber, calculateChecksum);
    }

    public static VhIpPacket BuildIcmpEchoRequestV4(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress,
         byte[] payload, ushort identifier = 0, ushort sequenceNumber = 0, bool calculateChecksum = true)
    {
        if (sourceAddress.Length != 4 || destinationAddress.Length != 4)
            throw new ArgumentException("SourceAddress and DestinationAddress must be IPv4 addresses.");

        // ICMP echo request
        var ipPacket = BuildIcmpV4(sourceAddress, destinationAddress, payload);
        var icmp = ipPacket.ExtractIcmpV4();
        icmp.Type = IcmpV4Type.EchoRequest;
        icmp.Code = 0;
        icmp.SequenceNumber = sequenceNumber;
        icmp.Identifier = identifier;
        if (calculateChecksum)
            ipPacket.UpdateAllChecksums();

        return ipPacket;
    }

    public static VhIpPacket BuildIcmpEchoRequestV6(IPAddress sourceAddress, IPAddress destinationAddress,
        byte[] payload, ushort identifier = 0, ushort sequenceNumber = 0, bool calculateChecksum = true)
    {
        return BuildIcmpEchoRequestV6(
            sourceAddress.GetAddressBytes(), destinationAddress.GetAddressBytes(),
            payload, identifier, sequenceNumber, calculateChecksum);
    }

    public static VhIpPacket BuildIcmpEchoRequestV6(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress,
        byte[] payload, ushort identifier = 0, ushort sequenceNumber = 0, bool calculateChecksum = true)
    {
        if (sourceAddress.Length != 16 || destinationAddress.Length != 16)
            throw new ArgumentException("SourceAddress and DestinationAddress must be IPv6 addresses.");

        // ICMP echo request
        var ipPacket = BuildIcmpV6(sourceAddress, destinationAddress, payload);
        var icmp = ipPacket.ExtractIcmpV6();
        icmp.Type = IcmpV6Type.EchoRequest;
        icmp.Code = 0;
        icmp.SequenceNumber = sequenceNumber;
        icmp.Identifier = identifier;
        if (calculateChecksum)
            ipPacket.UpdateAllChecksums();

        return ipPacket;
    }

    public static VhIpPacket BuildTcpResetReply(VhIpPacket ipPacket, bool updateChecksum = false)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
        if (ipPacket.Protocol != VhIpProtocol.Tcp)
            throw new ArgumentException("packet is not TCP!", nameof(ipPacket));

        var tcpPacketOrg = ipPacket.ExtractTcp();

        var resetIpPacket = BuildTcp(
            ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan,
            tcpPacketOrg.SourcePort, tcpPacketOrg.SourcePort, [], []);

        var resetTcpPacket = resetIpPacket.ExtractTcp();
        resetTcpPacket.Reset = true;
        resetTcpPacket.WindowSize = 0;

        if (tcpPacketOrg is { Synchronize: true, Acknowledgment: false }) {
            resetTcpPacket.Acknowledgment = true;
            resetTcpPacket.SequenceNumber = 0;
            resetTcpPacket.AcknowledgmentNumber = tcpPacketOrg.SequenceNumber + 1;
        }
        else {
            resetTcpPacket.Acknowledgment = false;
            resetTcpPacket.AcknowledgmentNumber = tcpPacketOrg.AcknowledgmentNumber;
            resetTcpPacket.SequenceNumber = tcpPacketOrg.AcknowledgmentNumber;
        }

        if (updateChecksum)
            resetIpPacket.UpdateAllChecksums();

        return resetIpPacket;
    }

    public static VhIpPacket BuildDns(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress,
        int sourcePort, int destinationPort, string host, ushort? queryId = null)
    {
        queryId ??= (ushort)new Random().Next(ushort.MaxValue);
        var query = DnsResolver.BuildDnsQuery(queryId.Value, host);
        var dnsPacket = BuildUdp(
            sourceAddress: sourceAddress,
            destinationAddress: destinationAddress,
            sourcePort: sourcePort,
            destinationPort: destinationPort,
            query);

        return dnsPacket;
    }

    public static VhIpPacket BuildDns(IPEndPoint sourceEndPoint, IPEndPoint destinationEndPoint,
        string host, ushort? queryId = null)
    {
        return BuildDns(
            sourceAddress: sourceEndPoint.Address.GetAddressBytes(),
            destinationAddress: destinationEndPoint.Address.GetAddressBytes(),
            sourcePort: sourceEndPoint.Port,
            destinationPort: destinationEndPoint.Port,
            host,
            queryId);
    }

    public static VhIpPacket BuildIcmpUnreachableReply(VhIpPacket ipPacket, bool updateChecksum = true)
    {
        return ipPacket.Version == VhIpVersion.IPv6
            ? BuildIcmpV6Error(ipPacket, IcmpV6Type.DestinationUnreachable, IcmpV6Code.AddressUnreachable, 0, updateChecksum)
            : BuildIcmpV4Error(ipPacket, IcmpV4Type.DestinationUnreachable, IcmpV4Code.HostUnreachable, updateChecksum);
    }

    public static VhIpPacket BuildIcmpUnreachablePortReply(VhIpPacket ipPacket, bool updateChecksum = true)
    {
        return ipPacket.Version == VhIpVersion.IPv6
            ? BuildIcmpV6Error(ipPacket, IcmpV6Type.DestinationUnreachable, IcmpV6Code.PortUnreachable, 0, updateChecksum)
            : BuildIcmpV4Error(ipPacket, IcmpV4Type.DestinationUnreachable, IcmpV4Code.PortUnreachable, updateChecksum);
    }

    public static VhIpPacket BuildIcmpPacketTooBigReply(VhIpPacket ipPacket, ushort mtu, bool updateChecksum = true)
    {
        return ipPacket.Version == VhIpVersion.IPv6
            ? BuildIcmpV6Error(ipPacket, IcmpV6Type.PacketTooBig, IcmpV6Code.PacketTooBig, mtu, updateChecksum)
            : BuildIcmpV4Error(ipPacket, IcmpV4Type.DestinationUnreachable, IcmpV4Code.FragmentationNeeded);
    }

    public static VhIpPacket BuildIcmpV4Error(VhIpPacket ipPacket, IcmpV4Type icmpV4Type, IcmpV4Code code, bool updateChecksum = true)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

        // Copy original IP header (20 bytes) + 8 bytes of payload into ICMP payload
        var icmpIpPacket = BuildIcmpV4(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan,
            ipPacket.Buffer.Span[..28]);
        var icmpPacket = icmpIpPacket.ExtractIcmpV4();
        icmpPacket.Type = icmpV4Type;
        icmpPacket.Code = (byte)code;


        if (updateChecksum)
            icmpIpPacket.UpdateAllChecksums();

        return icmpIpPacket;
    }

    public static VhIpPacket BuildIcmpV6Error(VhIpPacket ipPacket, IcmpV6Type icmpV6Type, IcmpV6Code code, uint messageSpecific,
        bool updateChecksum = true)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

        // Copy original IP header (40 bytes) + 8 bytes of payload into ICMP payload
        var icmpIpPacket = BuildIcmpV6(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan,
            ipPacket.Buffer.Span[..48]);
        var icmpPacket = icmpIpPacket.ExtractIcmpV6();
        icmpPacket.Type = icmpV6Type;
        icmpPacket.Code = (byte)code;
        icmpPacket.MessageSpecific = messageSpecific;

        if (updateChecksum)
            icmpIpPacket.UpdateAllChecksums();

        return icmpIpPacket;
    }
}