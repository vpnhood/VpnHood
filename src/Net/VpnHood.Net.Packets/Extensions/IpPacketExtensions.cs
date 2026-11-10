using System.Net;
using System.Runtime.InteropServices;
using VpnHood.Net.Toolkit.Net;

// ReSharper disable OutParameterValueIsAlwaysDiscarded.Global

namespace VpnHood.Net.Packets.Extensions;

/// <summary>
/// Provides the upper layer of an IP packet, its endpoints and its checksums.
/// </summary>
/// <remarks>
/// An <c>Extract</c> method parses a received packet's payload; a <c>Build</c> method prepares the payload of a
/// packet made by <see cref="PacketBuilder.BuildIp(IPAddress, IPAddress, IpProtocol, int, System.Buffers.MemoryPool{byte}?)"/>.
/// Either one stores the result in <see cref="IpPacket.PayloadPacket"/> and returns the stored one on later calls,
/// whichever method made it. The protocol is checked on every call; whether the packet is a fragment, and the
/// payload's structure, only when the payload is first parsed.
/// </remarks>
public static class IpPacketExtensions
{
    /// <summary>
    /// Provides the upper layer, the endpoints and the checksums of an IP packet.
    /// </summary>
    /// <param name="ipPacket">The IP packet.</param>
    extension(IpPacket ipPacket)
    {
        /// <summary>
        /// Creates a copy of the packet in an array rented from <see cref="System.Buffers.ArrayPool{T}.Shared"/>.
        /// </summary>
        /// <returns>The copy, which owns its memory: dispose it to return the memory.</returns>
        /// <exception cref="ObjectDisposedException">The packet is disposed.</exception>
        public IpPacket Clone()
        {
            return PacketBuilder.Parse(ipPacket.Buffer.Span);
        }

        /// <summary>
        /// Gets the payload of a received packet as a UDP datagram.
        /// </summary>
        /// <returns>The datagram, over the packet's own bytes.</returns>
        /// <exception cref="InvalidDataException">The packet does not carry UDP, or it is a fragment, which holds only a piece of its datagram.</exception>
        /// <exception cref="ArgumentException">The payload is no valid UDP datagram.</exception>
        public UdpPacket ExtractUdp()
        {
            if (ipPacket.Protocol != IpProtocol.Udp)
                throw new InvalidDataException($"Invalid UDP packet. It is: {ipPacket.Protocol}");

            ipPacket.PayloadPacket ??= new UdpPacket(GetWholePayload(ipPacket), false);
            return (UdpPacket)ipPacket.PayloadPacket;
        }

        /// <summary>
        /// Prepares the payload of a newly built packet as a UDP datagram: clears its header and sets its length
        /// field.
        /// </summary>
        /// <remarks>
        /// Use it on a packet from <see cref="PacketBuilder"/>, whose build cleared the payload whole, so only the
        /// header is cleared here. On a received packet, use <see cref="ExtractUdp"/>.
        /// </remarks>
        /// <returns>The datagram, over the packet's own bytes.</returns>
        /// <exception cref="InvalidDataException">The packet does not carry UDP.</exception>
        /// <exception cref="ArgumentException">The payload is shorter than the 8-byte UDP header.</exception>
        public UdpPacket BuildUdp()
        {
            if (ipPacket.Protocol != IpProtocol.Udp)
                throw new InvalidDataException($"Invalid UDP packet. It is: {ipPacket.Protocol}");

            ipPacket.PayloadPacket ??= new UdpPacket(ipPacket.Payload, true);
            return (UdpPacket)ipPacket.PayloadPacket;
        }

        /// <summary>
        /// Gets the payload of a received packet as a TCP segment.
        /// </summary>
        /// <returns>The segment, over the packet's own bytes.</returns>
        /// <exception cref="InvalidDataException">The packet does not carry TCP, or it is a fragment, which holds only a piece of its segment.</exception>
        /// <exception cref="ArgumentException">The payload is no valid TCP segment.</exception>
        public TcpPacket ExtractTcp()
        {
            if (ipPacket.Protocol != IpProtocol.Tcp)
                throw new InvalidDataException($"Invalid TCP packet. It is: {ipPacket.Protocol}");

            ipPacket.PayloadPacket ??= new TcpPacket(GetWholePayload(ipPacket));
            return (TcpPacket)ipPacket.PayloadPacket;
        }

        /// <summary>
        /// Prepares the payload of a newly built packet as a TCP segment: clears its header and options and sets its
        /// data offset.
        /// </summary>
        /// <remarks>
        /// Use it on a packet from <see cref="PacketBuilder"/>, whose build cleared the payload whole, so only the
        /// header is cleared here. On a received packet, use <see cref="ExtractTcp"/>.
        /// </remarks>
        /// <param name="optionsLength">The length of the TCP options, a multiple of 4 from 0 to 40.</param>
        /// <returns>The segment, over the packet's own bytes.</returns>
        /// <exception cref="InvalidDataException">The packet does not carry TCP.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="optionsLength"/> is out of range or not a multiple of 4.</exception>
        /// <exception cref="ArgumentException">The payload is shorter than the TCP header with its options.</exception>
        public TcpPacket BuildTcp(int optionsLength = 0)
        {
            if (ipPacket.Protocol != IpProtocol.Tcp)
                throw new InvalidDataException($"Invalid TCP packet. It is: {ipPacket.Protocol}");

            ipPacket.PayloadPacket ??= new TcpPacket(ipPacket.Payload, optionsLength);
            return (TcpPacket)ipPacket.PayloadPacket;
        }

        /// <summary>
        /// Prepares the payload of a newly built packet as an ICMPv4 message: clears its 8-byte header.
        /// </summary>
        /// <remarks>
        /// Use it on a packet from <see cref="PacketBuilder"/>, whose build cleared the payload whole, so only the
        /// header is cleared here. On a received packet, use <see cref="ExtractIcmpV4"/>.
        /// </remarks>
        /// <returns>The message, over the packet's own bytes.</returns>
        /// <exception cref="InvalidDataException">The packet does not carry ICMPv4.</exception>
        /// <exception cref="ArgumentException">The payload is shorter than 8 bytes.</exception>
        public IcmpV4Packet BuildIcmpV4()
        {
            if (ipPacket.Protocol != IpProtocol.IcmpV4)
                throw new InvalidDataException($"Invalid IcmpV4 packet. It is: {ipPacket.Protocol}");

            ipPacket.PayloadPacket ??= new IcmpV4Packet(ipPacket.Payload, true);
            return (IcmpV4Packet)ipPacket.PayloadPacket;
        }

        /// <summary>
        /// Gets the payload of a received packet as an ICMPv4 message.
        /// </summary>
        /// <returns>The message, over the packet's own bytes.</returns>
        /// <exception cref="InvalidDataException">The packet does not carry ICMPv4, or it is a fragment.</exception>
        /// <exception cref="ArgumentException">The payload is shorter than 8 bytes.</exception>
        public IcmpV4Packet ExtractIcmpV4()
        {
            if (ipPacket.Protocol != IpProtocol.IcmpV4)
                throw new InvalidDataException($"Invalid IcmpV4 packet. It is: {ipPacket.Protocol}");

            ipPacket.PayloadPacket ??= new IcmpV4Packet(GetWholePayload(ipPacket), false);
            return (IcmpV4Packet)ipPacket.PayloadPacket;
        }

        /// <summary>
        /// Prepares the payload of a newly built packet as an ICMPv6 message: clears its 8-byte header.
        /// </summary>
        /// <remarks>
        /// Use it on a packet from <see cref="PacketBuilder"/>, whose build cleared the payload whole, so only the
        /// header is cleared here. On a received packet, use <see cref="ExtractIcmpV6"/>.
        /// </remarks>
        /// <returns>The message, over the packet's own bytes.</returns>
        /// <exception cref="InvalidDataException">The packet does not carry ICMPv6.</exception>
        /// <exception cref="ArgumentException">The payload is shorter than 8 bytes.</exception>
        public IcmpV6Packet BuildIcmpV6()
        {
            if (ipPacket.Protocol != IpProtocol.IcmpV6)
                throw new InvalidDataException($"Invalid IcmpV6 packet. It is: {ipPacket.Protocol}");

            ipPacket.PayloadPacket ??= new IcmpV6Packet(ipPacket.Payload, true);
            return (IcmpV6Packet)ipPacket.PayloadPacket;
        }

        /// <summary>
        /// Gets the payload of a received packet as an ICMPv6 message.
        /// </summary>
        /// <returns>The message, over the packet's own bytes.</returns>
        /// <exception cref="InvalidDataException">The packet does not carry ICMPv6, or it is a fragment.</exception>
        /// <exception cref="ArgumentException">The payload is shorter than 8 bytes.</exception>
        public IcmpV6Packet ExtractIcmpV6()
        {
            if (ipPacket.Protocol != IpProtocol.IcmpV6)
                throw new InvalidDataException($"Invalid IcmpV6 packet. It is: {ipPacket.Protocol}");

            ipPacket.PayloadPacket ??= new IcmpV6Packet(GetWholePayload(ipPacket), false);
            return (IcmpV6Packet)ipPacket.PayloadPacket;
        }

        /// <summary>
        /// Gets whether the packet goes to a multicast address, 224.0.0.0/4 or ff00::/8, with a protocol that can:
        /// UDP, ICMPv4 or ICMPv6.
        /// </summary>
        /// <value><see langword="true"/> if it does; otherwise, <see langword="false"/>.</value>
        public bool IsMulticast {
            get {
                // only UDP and ICMP packets can use multicast
                if (ipPacket.Protocol is not (IpProtocol.Udp or IpProtocol.IcmpV4 or IpProtocol.IcmpV6))
                    return false;

                // the header's first byte, as DestinationAddress would make an IPAddress for the packet
                var destination = ipPacket.DestinationAddressSpan;
                return ipPacket.IsV4 ? (destination[0] & 0xF0) == 0xE0 : destination[0] == 0xFF;
            }
        }

        /// <summary>
        /// Gets whether the packet goes to the IPv4 limited broadcast address, 255.255.255.255.
        /// </summary>
        /// <value><see langword="true"/> if it does; otherwise, <see langword="false"/>.</value>
        /// <remarks>
        /// A subnet's directed broadcast address is not recognized, and an IPv6 packet is never a broadcast.
        /// </remarks>
        public bool IsBroadcast => IPAddress.Broadcast.SpanEquals(ipPacket.DestinationAddressSpan);

        /// <summary>
        /// Gets the packet's source address with the port of its TCP or UDP payload.
        /// </summary>
        /// <returns>The source endpoint; its port is 0 for a protocol other than TCP and UDP.</returns>
        /// <exception cref="InvalidDataException">The packet is a TCP or UDP fragment.</exception>
        /// <exception cref="ArgumentException">The TCP or UDP payload is invalid.</exception>
        public IpEndPointValue GetSourceEndPoint()
        {
            return ipPacket.Protocol switch {
                IpProtocol.Tcp => new IpEndPointValue(ipPacket.SourceAddressValue, ipPacket.ExtractTcp().SourcePort),
                IpProtocol.Udp => new IpEndPointValue(ipPacket.SourceAddressValue, ipPacket.ExtractUdp().SourcePort),
                _ => new IpEndPointValue(ipPacket.SourceAddressValue, 0)
            };
        }

        /// <summary>
        /// Gets the packet's destination address with the port of its TCP or UDP payload.
        /// </summary>
        /// <returns>The destination endpoint; its port is 0 for a protocol other than TCP and UDP.</returns>
        /// <exception cref="InvalidDataException">The packet is a TCP or UDP fragment.</exception>
        /// <exception cref="ArgumentException">The TCP or UDP payload is invalid.</exception>
        public IpEndPointValue GetDestinationEndPoint() {
            return ipPacket.Protocol switch {
                IpProtocol.Tcp => new IpEndPointValue(ipPacket.DestinationAddressValue,
                    ipPacket.ExtractTcp().DestinationPort),
                IpProtocol.Udp => new IpEndPointValue(ipPacket.DestinationAddressValue,
                    ipPacket.ExtractUdp().DestinationPort),
                _ => new IpEndPointValue(ipPacket.DestinationAddressValue, 0)
            };
        }

        /// <summary>
        /// Sets the packet's source address, and the source port of its TCP or UDP payload.
        /// </summary>
        /// <remarks>
        /// No checksum is updated: call <see cref="UpdateAllChecksums"/> afterwards.
        /// </remarks>
        /// <param name="value">The new source endpoint; its port must be 0 for a protocol other than TCP and UDP.</param>
        /// <exception cref="ArgumentException">The address is not of the packet's IP version, or the TCP or UDP payload is invalid.</exception>
        /// <exception cref="InvalidOperationException">
        /// The port is not 0 and the protocol has no ports; the address has been set by then.
        /// </exception>
        /// <exception cref="InvalidDataException">The packet is a TCP or UDP fragment.</exception>
        public void SetSourceEndPoint(IpEndPointValue value)
        {
            ipPacket.SourceAddressValue = value.AddressValue;

            switch (ipPacket.Protocol) {
                case IpProtocol.Tcp:
                    ipPacket.ExtractTcp().SourcePort = (ushort)value.Port;
                    break;

                case IpProtocol.Udp:
                    ipPacket.ExtractUdp().SourcePort = (ushort)value.Port;
                    break;

                default:
                    if (value.Port != 0)
                        throw new InvalidOperationException(
                            $"Cannot set non-zero port {value.Port} for protocol {ipPacket.Protocol}. Only TCP and UDP support ports.");
                    break;
            }
        }

        /// <summary>
        /// Sets the packet's destination address, and the destination port of its TCP or UDP payload.
        /// </summary>
        /// <remarks>
        /// No checksum is updated: call <see cref="UpdateAllChecksums"/> afterwards.
        /// </remarks>
        /// <param name="value">The new destination endpoint; its port must be 0 for a protocol other than TCP and UDP.</param>
        /// <exception cref="ArgumentException">The address is not of the packet's IP version, or the TCP or UDP payload is invalid.</exception>
        /// <exception cref="InvalidOperationException">
        /// The port is not 0 and the protocol has no ports; the address has been set by then.
        /// </exception>
        /// <exception cref="InvalidDataException">The packet is a TCP or UDP fragment.</exception>
        public void SetDestinationEndPoint(IpEndPointValue value)
        {
            ipPacket.DestinationAddressValue = value.AddressValue;

            switch (ipPacket.Protocol) {
                case IpProtocol.Tcp:
                    ipPacket.ExtractTcp().DestinationPort = (ushort)value.Port;
                    break;

                case IpProtocol.Udp:
                    ipPacket.ExtractUdp().DestinationPort = (ushort)value.Port;
                    break;

                default:
                    if (value.Port != 0)
                        throw new InvalidOperationException(
                            $"Cannot set non-zero port {value.Port} for protocol {ipPacket.Protocol}. Only TCP and UDP support ports.");
                    break;
            }
        }

        /// <summary>
        /// Gets the packet's source and destination endpoints, with the ports of its TCP or UDP payload.
        /// </summary>
        /// <returns>The pair; a port is 0 for a protocol other than TCP and UDP.</returns>
        /// <exception cref="InvalidDataException">The packet is a TCP or UDP fragment.</exception>
        /// <exception cref="ArgumentException">The TCP or UDP payload is invalid.</exception>
        public IPEndPointPair GetEndPoints()
        {
            return new IPEndPointPair(
                ipPacket.GetSourceEndPoint().ToIPEndPoint(),
                ipPacket.GetDestinationEndPoint().ToIPEndPoint());
        }

        /// <summary>
        /// Updates every checksum the packet carries: an IPv4 header's, and that of a TCP, UDP, ICMPv4 or ICMPv6
        /// payload.
        /// </summary>
        /// <remarks>
        /// A fragment's payload checksum is left as it is, since it covers the whole datagram, which no fragment
        /// holds. A payload of another protocol is left as it is.
        /// </remarks>
        /// <exception cref="ArgumentException">The payload is not a valid packet of its protocol.</exception>
        public void UpdateAllChecksums()
        {
            if (ipPacket is IpV4Packet ipV4Packet)
                ipV4Packet.UpdateHeaderChecksum();

            // the upper layer's checksum covers the whole datagram, which no fragment holds
            if (ipPacket.IsFragment)
                return;

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

    /// <summary>
    /// Provides the checksum of a payload packet against the IP packet that carries it.
    /// </summary>
    /// <param name="payloadPacket">The payload packet.</param>
    extension(IChecksumPayloadPacket payloadPacket)
    {
        /// <summary>
        /// Checks the payload's checksum field, with the addresses of the IP packet that carries it.
        /// </summary>
        /// <param name="ipPacket">The IP packet whose payload this is.</param>
        /// <returns><see langword="true"/> if the checksum is valid; otherwise, <see langword="false"/>.</returns>
        public bool IsChecksumValid(IpPacket ipPacket)
        {
            return payloadPacket
                .IsChecksumValid(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan);
        }

        /// <summary>
        /// Computes the payload's checksum, with the addresses of the IP packet that carries it, without writing it.
        /// </summary>
        /// <remarks>
        /// The checksum field may be zeroed while the sum runs and restored after, so no other thread may use the
        /// packet meanwhile.
        /// </remarks>
        /// <param name="ipPacket">The IP packet whose payload this is.</param>
        /// <returns>The checksum, as a number to write in network byte order.</returns>
        public ushort ComputeChecksum(IpPacket ipPacket)
        {
            return payloadPacket
                .ComputeChecksum(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan);
        }

        /// <summary>
        /// Computes the payload's checksum, with the addresses of the IP packet that carries it, and writes it.
        /// </summary>
        /// <param name="ipPacket">The IP packet whose payload this is.</param>
        public void UpdateChecksum(IpPacket ipPacket)
        {
            payloadPacket
                .UpdateChecksum(ipPacket.SourceAddressSpan, ipPacket.DestinationAddressSpan);
        }
    }

    /// <summary>
    /// Provides the echo test and the raw buffer of an IP packet.
    /// </summary>
    /// <param name="ipPacket">The IP packet.</param>
    extension(IpPacket ipPacket)
    {
        /// <summary>
        /// Determines whether the packet is a whole ICMPv4 or ICMPv6 echo request or reply.
        /// </summary>
        /// <returns><see langword="true"/> if it is, and not a fragment; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="ArgumentException">The ICMP payload is shorter than 8 bytes.</exception>
        public bool IsIcmpEcho()
        {
            // a fragment holds no whole echo
            return ipPacket switch {
                // IPv4
                { Version: IpVersion.IPv4, Protocol: IpProtocol.IcmpV4 } =>
                    !ipPacket.IsFragment && ipPacket.ExtractIcmpV4().IsEcho,
                // IPv6
                { Version: IpVersion.IPv6, Protocol: IpProtocol.IcmpV6 } =>
                    !ipPacket.IsFragment && ipPacket.ExtractIcmpV6().IsEcho,
                _ => false
            };
        }

        /// <summary>
        /// Gets an array that holds the packet from its first byte: the array behind the packet's buffer when the
        /// buffer starts at that array's first byte, or else <paramref name="backupBuffer"/>, with the packet copied
        /// in.
        /// </summary>
        /// <remarks>
        /// The returned array may be the packet's own and longer than the packet: read only its first
        /// <paramref name="length"/> bytes, and only while the packet is alive.
        /// </remarks>
        /// <param name="backupBuffer">The array to copy the packet into when its own cannot be used; at least as long as the packet.</param>
        /// <param name="length">When this method returns, the packet's length.</param>
        /// <returns>The array.</returns>
        /// <exception cref="ArgumentException"><paramref name="backupBuffer"/> is needed and is shorter than the packet.</exception>
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

        /// <summary>
        /// Gets an array that holds the packet: the array behind the packet's buffer, or else
        /// <paramref name="backupBuffer"/>, with the packet copied in.
        /// </summary>
        /// <remarks>
        /// The returned array may be the packet's own: read only the <paramref name="length"/> bytes from
        /// <paramref name="offset"/>, and only while the packet is alive.
        /// </remarks>
        /// <param name="backupBuffer">The array to copy the packet into when its own cannot be used; at least as long as the packet.</param>
        /// <param name="offset">When this method returns, where the packet starts in the array.</param>
        /// <param name="length">When this method returns, the packet's length.</param>
        /// <returns>The array.</returns>
        /// <exception cref="ArgumentException"><paramref name="backupBuffer"/> is needed and is shorter than the packet.</exception>
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

    // a fragment's payload is a piece of its upper-layer packet, which reads as one only by chance
    private static Memory<byte> GetWholePayload(IpPacket ipPacket)
    {
        return ipPacket.IsFragment
            ? throw new InvalidDataException($"A fragment holds only a piece of its {ipPacket.Protocol} packet.")
            : ipPacket.Payload;
    }
}
