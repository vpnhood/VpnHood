using System.Net;
using PacketDotNet;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Net;
using IcmpV4Packet = PacketDotNet.IcmpV4Packet;
using IcmpV6Packet = PacketDotNet.IcmpV6Packet;
using TcpPacket = PacketDotNet.TcpPacket;
using UdpPacket = PacketDotNet.UdpPacket;


namespace VpnHood.Test.Packets;

// ReSharper disable once InconsistentNaming
public static class NetPacketExtensions
{
    extension(IPPacket ipPacket)
    {
        public IPPacket Clone()
        {
            return Packet.ParsePacket(LinkLayers.Raw, ipPacket.Bytes).Extract<IPPacket>();
        }

        public IcmpV4Packet ExtractIcmpV4()
        {
            return ipPacket.Extract<IcmpV4Packet>() ??
                   throw new InvalidDataException($"Invalid IcmpV4 packet. It is: {ipPacket.Protocol}");
        }

        public IcmpV6Packet ExtractIcmpV6()
        {
            return ipPacket.Extract<IcmpV6Packet>() ??
                   throw new InvalidDataException($"Invalid IcmpV6 packet. It is: {ipPacket.Protocol}");
        }

        public UdpPacket ExtractUdp()
        {
            return ipPacket.Extract<UdpPacket>() ??
                   throw new InvalidDataException($"Invalid UDP packet. It is: {ipPacket.Protocol}");
        }

        public TcpPacket ExtractTcp()
        {
            return ipPacket.Extract<TcpPacket>() ??
                   throw new InvalidDataException($"Invalid TCP packet. It is: {ipPacket.Protocol}");
        }

        public bool IsMulticast()
        {
            // only UDP and ICMPv6 packets can use multicast
            if (ipPacket.Protocol != ProtocolType.Udp && ipPacket.Protocol != ProtocolType.IcmpV6)
                return false;

            return
                (ipPacket.Version == IPVersion.IPv4 &&
                 IpNetwork.MulticastNetworkV4.Contains(ipPacket.DestinationAddress)) ||
                (ipPacket.Version == IPVersion.IPv6 &&
                 IpNetwork.MulticastNetworkV6.Contains(ipPacket.DestinationAddress));
        }

        public IPEndPointPair GetEndPoints()
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

        public void UpdateIpChecksum()
        {
            // ICMPv6 & UDP & TCP checksum needs to be updated if IPv6 packet changes because they have pseudo header
            if (ipPacket.Protocol is ProtocolType.IcmpV6 or ProtocolType.Udp or ProtocolType.Tcp) {
                ipPacket.UpdatePayloadChecksum();
            }

            // ipv4 packet checksum needs to be updated if IPv4 packet changes
            if (ipPacket is IPv4Packet ipV4Packet) {
                ipV4Packet.UpdateIPChecksum(); // it is not the extension
                ipPacket.UpdateCalculatedValues();
            }
        }

        public void UpdatePayloadChecksum(bool throwIfNotSupported = false)
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
                    var icmpPacket = ipPacket.ExtractIcmpV4();
                    icmpPacket.UpdateIcmpChecksum();
                    icmpPacket.UpdateCalculatedValues();
                    break;

                case ProtocolType.IcmpV6:
                    var icmpV6Packet = ipPacket.ExtractIcmpV6();
                    // icmpV6Packet.UpdateIcmpChecksum(); // it has problem
                    icmpV6Packet.Checksum = 0;
                    icmpV6Packet.Checksum = PacketUtil.ComputeChecksum(
                        ipPacket.SourceAddress.GetAddressBytesFast(stackalloc byte[16]),
                        ipPacket.DestinationAddress.GetAddressBytesFast(stackalloc byte[16]),
                        (byte)ProtocolType.IcmpV6,
                        icmpV6Packet.Bytes);
                    icmpV6Packet.UpdateCalculatedValues();
                    break;

                default:
                    if (throwIfNotSupported)
                        throw new NotSupportedException("Does not support this packet.");
                    break;
            }
        }

        public void UpdateAllChecksums(bool throwIfNotSupported = false)
        {
            ipPacket.UpdatePayloadChecksum(throwIfNotSupported);

            // ipv4 packet checksum needs to be updated if IPv4 packet changes
            if (ipPacket is IPv4Packet ipV4Packet) {
                ipV4Packet.UpdateIPChecksum(); // it is not the extension
                ipV4Packet.UpdateCalculatedValues();
            }
        }
    }
}