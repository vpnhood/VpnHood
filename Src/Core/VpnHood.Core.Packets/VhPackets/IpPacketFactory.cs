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
        return ipPacket;
    }

    public static VhIpPacket BuildUdp(IPEndPoint sourceEndPoint, IPEndPoint destinationEndPoint, ReadOnlySpan<byte> payload)
    {
        return BuildUdp(
            sourceEndPoint.Address.GetAddressBytes(), destinationEndPoint.Address.GetAddressBytes(),
            sourceEndPoint.Port, destinationEndPoint.Port,
            payload);
    }


    public static VhIpPacket BuildUdp(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress,
        int sourcePort, int destinationPort, ReadOnlySpan<byte> payload)
    {
        var ipPacket = BuildIp(sourceAddress, destinationAddress, VhIpProtocol.Udp, payload.Length);
        var udpPacket = ipPacket.ExtractUdp(true);
        udpPacket.SourcePort = (ushort)sourcePort;
        udpPacket.DestinationPort = (ushort)destinationPort;
        payload.CopyTo(udpPacket.Payload.Span);
        return ipPacket;
    }

}