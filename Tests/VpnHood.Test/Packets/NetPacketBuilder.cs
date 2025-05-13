using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using PacketDotNet;
using PacketDotNet.Utils;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Test.Packets;

public static class NetPacketBuilder
{
    public static byte[] RandomPacket(bool isV4)
    {
        return IPPacket.RandomPacket(isV4 ? IPVersion.IPv4 : IPVersion.IPv6).Bytes;
    }

    public static IPPacket BuildIpPacket(IPAddress sourceAddress, IPAddress destinationAddress)
    {
        if (sourceAddress.AddressFamily != destinationAddress.AddressFamily)
            throw new InvalidOperationException(
                $"{nameof(sourceAddress)} and {nameof(destinationAddress)}  address family must be same!");

        return sourceAddress.AddressFamily switch {
            AddressFamily.InterNetwork => new IPv4Packet(sourceAddress, destinationAddress),
            AddressFamily.InterNetworkV6 => new IPv6Packet(sourceAddress, destinationAddress),
            _ => throw new NotSupportedException($"{sourceAddress.AddressFamily} is not supported!")
        };
    }

    public static IPPacket Parse(byte[] ipPacketBuffer)
    {
        return Packet.ParsePacket(LinkLayers.Raw, ipPacketBuffer).Extract<IPPacket>();
    }

    public static IPPacket BuildIcmpPacketTooBigReply(IPPacket ipPacket, int mtu, bool updateChecksum)
    {
        return ipPacket.Version == IPVersion.IPv6
            ? BuildIcmpUnreachableReplyV6(ipPacket, IcmpV6Type.PacketTooBig, 0, mtu, updateChecksum)
            : BuildIcmpUnreachableReplyV4(ipPacket, IcmpV4TypeCode.UnreachableFragmentationNeeded, mtu, updateChecksum);
    }

    private static IPPacket BuildIcmpUnreachableReplyV6(IPPacket ipPacket, IcmpV6Type icmpV6Type, byte code, int reserved, bool updateChecksum)
    {
        if (ipPacket is null)
            throw new ArgumentNullException(nameof(ipPacket));

        const int headerSize = 4;
        var icmpDataLen = Math.Min(ipPacket.TotalLength, 1280 - 40 - 8);
        var buffer = new byte[4 + 4 + icmpDataLen];
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(6, 2), (ushort)reserved);
        Array.Copy(ipPacket.Bytes, 0, buffer, 8, icmpDataLen);

        // PacketDotNet doesn't support IcmpV6Packet properly
        var icmpPacket = new IcmpV6Packet(new ByteArraySegment(new byte[headerSize])) {
            Type = icmpV6Type,
            Code = code,
            PayloadData = buffer[headerSize..]
        };


        var newIpPacket = new IPv6Packet(ipPacket.DestinationAddress, ipPacket.SourceAddress) {
            PayloadPacket = icmpPacket,
            Protocol = ProtocolType.IcmpV6,
            TimeToLive = 64,
            PayloadLength = (ushort)icmpPacket.TotalPacketLength
        };

        if (updateChecksum)
            newIpPacket.UpdateAllChecksums();
        return newIpPacket;
    }

    private static IPPacket BuildIcmpUnreachableReplyV4(IPPacket ipPacket, IcmpV4TypeCode typeCode, int reserved,
        bool updateChecksum)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

        // packet is too big
        const int headerSize = 8;
        var icmpDataLen = Math.Min(ipPacket.TotalLength, 20 + 8);
        var buffer = new byte[headerSize + icmpDataLen];

        // Copy original IP header (20 bytes) + 8 bytes of payload into ICMP payload
        var icmpPacket = new IcmpV4Packet(new ByteArraySegment(buffer)) {
            TypeCode = typeCode,
            Sequence = 0
        };
        Array.Copy(ipPacket.Bytes, 0, buffer, headerSize, icmpDataLen);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(6, 2), (ushort)reserved);

        var newIpPacket = new IPv4Packet(ipPacket.DestinationAddress, ipPacket.SourceAddress) {
            PayloadPacket = icmpPacket,
            Protocol = ProtocolType.Icmp,
            TimeToLive = 64,
            FragmentFlags = 0
        };

        if (updateChecksum)
            newIpPacket.UpdateAllChecksums();
        return newIpPacket;
    }
}