using System.Buffers;
using System.Net;

namespace VpnHood.Core.Packets.VhPackets;

public static class IpPacketFactory
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
        var ipPacket = BuildIp(sourceAddress, destinationAddress, VhIpProtocol.IcmpV4, 8 + payload.Length);
        var icmp = ipPacket.ExtractIcmpV4();
        icmp.TypeCode = IcmpV4TypeCode.EchoRequest;
        icmp.SequenceNumber = sequenceNumber;
        icmp.Identifier = identifier;
        if (calculateChecksum)
            ipPacket.UpdateAllChecksums();

        return ipPacket;
    }


}