using System.Net;
using System.Runtime.InteropServices;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Packets.Extensions;

public static class IpPacketExtensions
{
    extension(IpPacket ipPacket)
    {
        public IpPacket Clone()
        {
            return PacketBuilder.Parse(ipPacket.Buffer.Span);
        }

        public UdpPacket ExtractUdp()
        {
            if (ipPacket.Protocol != IpProtocol.Udp)
                throw new InvalidDataException($"Invalid UDP packet. It is: {ipPacket.Protocol}");

            ipPacket.PayloadPacket ??= new UdpPacket(ipPacket.Payload, false);
            return (UdpPacket)ipPacket.PayloadPacket;
        }

        public UdpPacket BuildUdp()
        {
            if (ipPacket.Protocol != IpProtocol.Udp)
                throw new InvalidDataException($"Invalid UDP packet. It is: {ipPacket.Protocol}");

            ipPacket.PayloadPacket ??= new UdpPacket(ipPacket.Payload, true);
            return (UdpPacket)ipPacket.PayloadPacket;
        }

        public TcpPacket ExtractTcp()
        {
            if (ipPacket.Protocol != IpProtocol.Tcp)
                throw new InvalidDataException($"Invalid TCP packet. It is: {ipPacket.Protocol}");

            ipPacket.PayloadPacket ??= new TcpPacket(ipPacket.Payload);
            return (TcpPacket)ipPacket.PayloadPacket;
        }

        public TcpPacket BuildTcp(int optionsLength = 0)
        {
            if (ipPacket.Protocol != IpProtocol.Tcp)
                throw new InvalidDataException($"Invalid TCP packet. It is: {ipPacket.Protocol}");

            ipPacket.PayloadPacket ??= new TcpPacket(ipPacket.Payload, optionsLength);
            return (TcpPacket)ipPacket.PayloadPacket;
        }

        public IcmpV4Packet BuildIcmpV4()
        {
            if (ipPacket.Protocol != IpProtocol.IcmpV4)
                throw new InvalidDataException($"Invalid IcmpV4 packet. It is: {ipPacket.Protocol}");

            ipPacket.PayloadPacket ??= new IcmpV4Packet(ipPacket.Payload, true);
            return (IcmpV4Packet)ipPacket.PayloadPacket;
        }

        public IcmpV4Packet ExtractIcmpV4()
        {
            if (ipPacket.Protocol != IpProtocol.IcmpV4)
                throw new InvalidDataException($"Invalid IcmpV4 packet. It is: {ipPacket.Protocol}");

            ipPacket.PayloadPacket ??= new IcmpV4Packet(ipPacket.Payload, false);
            return (IcmpV4Packet)ipPacket.PayloadPacket;
        }

        public IcmpV6Packet BuildIcmpV6()
        {
            if (ipPacket.Protocol != IpProtocol.IcmpV6)
                throw new InvalidDataException($"Invalid IcmpV6 packet. It is: {ipPacket.Protocol}");

            ipPacket.PayloadPacket ??= new IcmpV6Packet(ipPacket.Payload, true);
            return (IcmpV6Packet)ipPacket.PayloadPacket;
        }

        public IcmpV6Packet ExtractIcmpV6()
        {
            if (ipPacket.Protocol != IpProtocol.IcmpV6)
                throw new InvalidDataException($"Invalid IcmpV6 packet. It is: {ipPacket.Protocol}");

            ipPacket.PayloadPacket ??= new IcmpV6Packet(ipPacket.Payload, false);
            return (IcmpV6Packet)ipPacket.PayloadPacket;
        }

        public bool IsMulticast()
        {
            // only UDP and ICMPv6 packets can use multicast
            return ipPacket.Protocol is IpProtocol.Udp or IpProtocol.IcmpV4 or IpProtocol.IcmpV6 &&
                   ipPacket.DestinationAddress.IsMulticast();
        }

        public bool IsBroadcast()
        {
            return IPAddress.Broadcast.SpanEquals(ipPacket.DestinationAddressSpan);
        }

        public IPEndPointPair GetEndPoints()
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

        public void UpdateAllChecksums()
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
    }


    extension(IChecksumPayloadPacket payloadPacket)
    {
        public bool IsChecksumValid(IpPacket ipPacket)
        {
            return payloadPacket
                .IsChecksumValid(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan);
        }

        public ushort ComputeChecksum(IpPacket ipPacket)
        {
            return payloadPacket
                .ComputeChecksum(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan);
        }

        public void UpdateChecksum(IpPacket ipPacket)
        {
            payloadPacket
                .UpdateChecksum(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan);
        }
    }

    extension(IpPacket ipPacket)
    {
        public bool IsV4()
        {
            return ipPacket.Version == IpVersion.IPv4;
        }

        public bool IsV6()
        {
            return ipPacket.Version == IpVersion.IPv6;
        }

        public bool IsIcmpEcho()
        {
            return ipPacket switch {
                // IPv4
                { Version: IpVersion.IPv4, Protocol: IpProtocol.IcmpV4 } => ipPacket.ExtractIcmpV4().IsEcho,
                // IPv6
                { Version: IpVersion.IPv6, Protocol: IpProtocol.IcmpV6 } => ipPacket.ExtractIcmpV6().IsEcho,
                _ => false
            };
        }

        public byte[] GetUnderlyingBufferUnsafe(byte[] backupBuffer, out int length)
        {
            if (MemoryMarshal.TryGetArray<byte>(ipPacket.Buffer, out var segment) && segment.Offset == 0) {
                length = segment.Count;
                return segment.Array!;
            }

            ipPacket.Buffer.CopyTo(backupBuffer);
            length = ipPacket.Buffer.Length;
            return backupBuffer;
        }

        public byte[] GetUnderlyingBufferUnsafe(byte[] backupBuffer, out int offset, out int length)
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
}