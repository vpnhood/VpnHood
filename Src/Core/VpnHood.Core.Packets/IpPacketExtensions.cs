using System.Net;
using PacketDotNet;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Packets;

public static class IpPacketExtensions
{
    public static IPPacket ClonePacket(this IPPacket ipPacket)
    {
        return Packet.ParsePacket(LinkLayers.Raw, ipPacket.Bytes).Extract<IPPacket>();
    }
    public static IcmpV4Packet ExtractIcmp(this IPPacket ipPacket)
    {
        return ipPacket.Extract<IcmpV4Packet>() ??
               throw new InvalidDataException($"Invalid IcmpV4 packet. It is: {ipPacket.Protocol}");
    }

    public static IcmpV6Packet ExtractIcmpV6(this IPPacket ipPacket)
    {
        return ipPacket.Extract<IcmpV6Packet>() ??
               throw new InvalidDataException($"Invalid IcmpV6 packet. It is: {ipPacket.Protocol}");
    }

    public static UdpPacket ExtractUdp(this IPPacket ipPacket)
    {
        return ipPacket.Extract<UdpPacket>() ??
               throw new InvalidDataException($"Invalid UDP packet. It is: {ipPacket.Protocol}");
    }

    public static TcpPacket ExtractTcp(this IPPacket ipPacket)
    {
        return ipPacket.Extract<TcpPacket>() ??
               throw new InvalidDataException($"Invalid TCP packet. It is: {ipPacket.Protocol}");
    }

    public static IPEndPointPair GetEndPoints(this IPPacket ipPacket)
    {
        if (ipPacket.Protocol == ProtocolType.Tcp) {
            var tcpPacket = ExtractTcp(ipPacket);
            return new IPEndPointPair(
                new IPEndPoint(ipPacket.SourceAddress, tcpPacket.SourcePort),
                new IPEndPoint(ipPacket.DestinationAddress, tcpPacket.DestinationPort));
        }

        if (ipPacket.Protocol == ProtocolType.Udp) {
            var udpPacket = ExtractUdp(ipPacket);
            return new IPEndPointPair(
                new IPEndPoint(ipPacket.SourceAddress, udpPacket.SourcePort),
                new IPEndPoint(ipPacket.DestinationAddress, udpPacket.DestinationPort));
        }

        return new IPEndPointPair(
            new IPEndPoint(ipPacket.SourceAddress, 0),
            new IPEndPoint(ipPacket.DestinationAddress, 0));
    }

    public static void UpdateIpChecksum(this IPPacket ipPacket)
    {
        // ICMPv6 checksum needs to be updated if IPv6 packet changes
        if (ipPacket.Protocol == ProtocolType.IcmpV6)
            ipPacket.UpdateAllChecksums();

        // ipv4 packet checksum needs to be updated if IPv4 packet changes
        if (ipPacket is IPv4Packet ipV4Packet) {
            ipV4Packet.UpdateIPChecksum();
            ipPacket.UpdateCalculatedValues();
        }
    }
    public static void UpdatePayloadChecksum(this IPPacket ipPacket, bool throwIfNotSupported = true)
    {
        switch (ipPacket.Protocol) {
            case ProtocolType.Tcp:
                var tcpPacket = ipPacket.ExtractTcp();
                tcpPacket.UpdateTcpChecksum();
                tcpPacket.UpdateCalculatedValues();
                break;

            case ProtocolType.Udp:
                var udpPacket = ipPacket.ExtractUdp();
                udpPacket.UpdateUdpChecksum();
                udpPacket.UpdateCalculatedValues();
                break;

            case ProtocolType.Icmp:
                var icmpPacket = ipPacket.ExtractIcmp();
                icmpPacket.UpdateIcmpChecksum();
                icmpPacket.UpdateCalculatedValues();
                break;

            case ProtocolType.IcmpV6:
                var icmpV6Packet = ipPacket.ExtractIcmpV6();
                icmpV6Packet.UpdateIcmpChecksum();
                icmpV6Packet.UpdateCalculatedValues();
                break;

            default:
                if (throwIfNotSupported)
                    throw new NotSupportedException("Does not support this packet!");
                break;
        }
    }

    public static void UpdateAllChecksums(this IPPacket ipPacket, bool throwIfNotSupported = true)
    {
        ipPacket.UpdatePayloadChecksum(throwIfNotSupported);
        ipPacket.UpdateIpChecksum();
    }
}