using System.Net;
using System.Runtime.InteropServices;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Packets;

public static class IpPacketExtensions
{
    public static IpPacket Clone(this IpPacket ipPacket)
    {
        return PacketBuilder.Parse(ipPacket.Buffer.Span);
    }

    public static UdpPacket ExtractUdp(this IpPacket ipPacket)
    {
        if (ipPacket.Protocol != IpProtocol.Udp)
            throw new InvalidDataException($"Invalid UDP packet. It is: {ipPacket.Protocol}");

        ipPacket.PayloadPacket ??= new UdpPacket(ipPacket.Payload, false);
        return (UdpPacket)ipPacket.PayloadPacket;
    }

    public static UdpPacket BuildUdp(this IpPacket ipPacket)
    {
        if (ipPacket.Protocol != IpProtocol.Udp)
            throw new InvalidDataException($"Invalid UDP packet. It is: {ipPacket.Protocol}");

        ipPacket.PayloadPacket ??= new UdpPacket(ipPacket.Payload, true);
        return (UdpPacket)ipPacket.PayloadPacket;
    }

    public static TcpPacket ExtractTcp(this IpPacket ipPacket)
    {
        if (ipPacket.Protocol != IpProtocol.Tcp)
            throw new InvalidDataException($"Invalid TCP packet. It is: {ipPacket.Protocol}");

        ipPacket.PayloadPacket ??= new TcpPacket(ipPacket.Payload);
        return (TcpPacket)ipPacket.PayloadPacket;
    }

    public static TcpPacket BuildTcp(this IpPacket ipPacket, int optionsLength = 0)
    {
        if (ipPacket.Protocol != IpProtocol.Tcp)
            throw new InvalidDataException($"Invalid TCP packet. It is: {ipPacket.Protocol}");

        ipPacket.PayloadPacket ??= new TcpPacket(ipPacket.Payload, optionsLength);
        return (TcpPacket)ipPacket.PayloadPacket;
    }

    public static IcmpV4Packet BuildIcmpV4(this IpPacket ipPacket)
    {
        if (ipPacket.Protocol != IpProtocol.IcmpV4)
            throw new InvalidDataException($"Invalid IcmpV4 packet. It is: {ipPacket.Protocol}");

        ipPacket.PayloadPacket ??= new IcmpV4Packet(ipPacket.Payload, true);
        return (IcmpV4Packet)ipPacket.PayloadPacket;
    }

    public static IcmpV4Packet ExtractIcmpV4(this IpPacket ipPacket)
    {
        if (ipPacket.Protocol != IpProtocol.IcmpV4)
            throw new InvalidDataException($"Invalid IcmpV4 packet. It is: {ipPacket.Protocol}");

        ipPacket.PayloadPacket ??= new IcmpV4Packet(ipPacket.Payload, false);
        return (IcmpV4Packet)ipPacket.PayloadPacket;
    }

    public static IcmpV6Packet BuildIcmpV6(this IpPacket ipPacket)
    {
        if (ipPacket.Protocol != IpProtocol.IcmpV6)
            throw new InvalidDataException($"Invalid IcmpV6 packet. It is: {ipPacket.Protocol}");

        ipPacket.PayloadPacket ??= new IcmpV6Packet(ipPacket.Payload, true);
        return (IcmpV6Packet)ipPacket.PayloadPacket;
    }

    public static IcmpV6Packet ExtractIcmpV6(this IpPacket ipPacket)
    {
        if (ipPacket.Protocol != IpProtocol.IcmpV6)
            throw new InvalidDataException($"Invalid IcmpV6 packet. It is: {ipPacket.Protocol}");

        ipPacket.PayloadPacket ??= new IcmpV6Packet(ipPacket.Payload, false);
        return (IcmpV6Packet)ipPacket.PayloadPacket;
    }

    public static bool IsMulticast(this IpPacket ipPacket)
    {
        // only UDP and ICMPv6 packets can use multicast
        return ipPacket.Protocol is IpProtocol.Udp or IpProtocol.IcmpV4 or IpProtocol.IcmpV6 &&
               ipPacket.DestinationAddress.IsMulticast();

    }

    public static IPEndPointPair GetEndPoints(this IpPacket ipPacket)
    {
        if (ipPacket.Protocol == IpProtocol.Tcp) {
            var tcpPacket = ipPacket.ExtractTcp();
            return new IPEndPointPair(
                new IPEndPoint(ipPacket.SourceAddress, tcpPacket.SourcePort),
                new IPEndPoint(ipPacket.DestinationAddress, tcpPacket.DestinationPort));
        }

        if (ipPacket.Protocol == IpProtocol.Udp) {
            var udpPacket = ipPacket.ExtractUdp();
            return new IPEndPointPair(
                new IPEndPoint(ipPacket.SourceAddress, udpPacket.SourcePort),
                new IPEndPoint(ipPacket.DestinationAddress, udpPacket.DestinationPort));
        }

        return new IPEndPointPair(
            new IPEndPoint(ipPacket.SourceAddress, 0),
            new IPEndPoint(ipPacket.DestinationAddress, 0));
    }

    public static void UpdateAllChecksums(this IpPacket ipPacket)
    {
        if (ipPacket is IpV4Packet ipV4Packet)
            ipV4Packet.UpdateHeaderChecksum(ipV4Packet);

        if (ipPacket.Protocol == IpProtocol.Udp)
            ipPacket.ExtractUdp().UpdateChecksum(ipPacket);

        if (ipPacket.Protocol == IpProtocol.Tcp)
            ipPacket.ExtractTcp().UpdateChecksum(ipPacket);

        if (ipPacket.Protocol == IpProtocol.IcmpV4)
            ipPacket.ExtractIcmpV4().UpdateChecksum(ipPacket);

        if (ipPacket.Protocol == IpProtocol.IcmpV6)
            ipPacket.ExtractIcmpV6().UpdateChecksum(ipPacket);

    }

    public static bool IsChecksumValid(this IChecksumPayloadPacket payloadPacket, IpPacket ipPacket)
    {
        return payloadPacket
            .IsChecksumValid(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan);
    }

    public static ushort ComputeChecksum(this IChecksumPayloadPacket payloadPacket, IpPacket ipPacket)
    {
        return payloadPacket
            .ComputeChecksum(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan);
    }

    public static void UpdateChecksum(this IChecksumPayloadPacket payloadPacket, IpPacket ipPacket)
    {
        payloadPacket
            .UpdateChecksum(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan);
    }

    public static bool IsV4(this IpPacket ipPacket)
    {
        return ipPacket.Version == IpVersion.IPv4;
    }

    public static bool IsV6(this IpPacket ipPacket)
    {
        return ipPacket.Version == IpVersion.IPv6;
    }

    public static bool IsIcmpEcho(this IpPacket ipPacket)
    {
        return ipPacket switch {
            // IPv4
            { Version: IpVersion.IPv4, Protocol: IpProtocol.IcmpV4 } => ipPacket.ExtractIcmpV4().IsEcho,
            // IPv6
            { Version: IpVersion.IPv6, Protocol: IpProtocol.IcmpV6 } => ipPacket.ExtractIcmpV6().IsEcho,
            _ => false
        };
    }

    public static byte[] GetUnderlyingBufferUnsafe(this IpPacket ipPacket, byte[] backupBuffer, out int length)
    {
        if (MemoryMarshal.TryGetArray<byte>(ipPacket.Buffer, out var segment) && segment.Offset == 0) {
            length = segment.Count;
            return segment.Array!;
        }

        ipPacket.Buffer.CopyTo(backupBuffer);
        length = ipPacket.Buffer.Length;
        return backupBuffer;
    }

    public static byte[] GetUnderlyingBufferUnsafe(this IpPacket ipPacket,
        byte[] backupBuffer, out int offset, out int length)
    {
        if (MemoryMarshal.TryGetArray<byte>(ipPacket.Buffer, out var segment)) {
            offset = segment.Offset;
            length = segment.Count;
            return segment.Array!;
        }

        ipPacket.Buffer.CopyTo(backupBuffer);
        offset = 0;
        length = ipPacket.Buffer.Length;
        return backupBuffer;
    }
}