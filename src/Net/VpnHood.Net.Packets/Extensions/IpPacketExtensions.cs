using System.Buffers.Binary;
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
        /// <exception cref="InvalidOperationException">The port is not 0 and the protocol has no ports.</exception>
        /// <exception cref="InvalidDataException">The packet is a TCP or UDP fragment.</exception>
        public void SetSourceEndPoint(IpEndPointValue value)
        {
            // each case reads its payload, or checks the port, before it sets the address, which checks before it
            // writes: a refused endpoint leaves the packet as it was
            switch (ipPacket.Protocol) {
                case IpProtocol.Tcp:
                    var tcpPacket = ipPacket.ExtractTcp();
                    ipPacket.SourceAddressValue = value.AddressValue;
                    tcpPacket.SourcePort = (ushort)value.Port;
                    break;

                case IpProtocol.Udp:
                    var udpPacket = ipPacket.ExtractUdp();
                    ipPacket.SourceAddressValue = value.AddressValue;
                    udpPacket.SourcePort = (ushort)value.Port;
                    break;

                default:
                    if (value.Port != 0)
                        throw new InvalidOperationException(
                            $"Cannot set non-zero port {value.Port} for protocol {ipPacket.Protocol}. Only TCP and UDP support ports.");

                    ipPacket.SourceAddressValue = value.AddressValue;
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
        /// <exception cref="InvalidOperationException">The port is not 0 and the protocol has no ports.</exception>
        /// <exception cref="InvalidDataException">The packet is a TCP or UDP fragment.</exception>
        public void SetDestinationEndPoint(IpEndPointValue value)
        {
            // each case reads its payload, or checks the port, before it sets the address, which checks before it
            // writes: a refused endpoint leaves the packet as it was
            switch (ipPacket.Protocol) {
                case IpProtocol.Tcp:
                    var tcpPacket = ipPacket.ExtractTcp();
                    ipPacket.DestinationAddressValue = value.AddressValue;
                    tcpPacket.DestinationPort = (ushort)value.Port;
                    break;

                case IpProtocol.Udp:
                    var udpPacket = ipPacket.ExtractUdp();
                    ipPacket.DestinationAddressValue = value.AddressValue;
                    udpPacket.DestinationPort = (ushort)value.Port;
                    break;

                default:
                    if (value.Port != 0)
                        throw new InvalidOperationException(
                            $"Cannot set non-zero port {value.Port} for protocol {ipPacket.Protocol}. Only TCP and UDP support ports.");

                    ipPacket.DestinationAddressValue = value.AddressValue;
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
        /// holds; after a change of an address, <see cref="UpdatePayloadChecksum"/> brings it in line. A payload of
        /// another protocol is left as it is.
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

        /// <summary>
        /// Updates the checksum of the packet's TCP, UDP or ICMPv6 payload for a change of one of the packet's
        /// addresses, from the change alone (RFC 1624), without reading the rest of the payload.
        /// </summary>
        /// <remarks>
        /// It is for a fragment, whose payload checksum <see cref="UpdateAllChecksums"/> leaves as it is: the fragment
        /// that holds the checksum takes the change, and one that holds none is left as it is. That is the datagram's
        /// first fragment, or, in IPv4, the second where the first ends before a TCP checksum. An IPv6 fragment after
        /// the first is never changed: where its piece lies in the payload is not known from it alone, and the first
        /// holds the payload's header (RFC 7112). Nothing changes either for a payload of another protocol, whose
        /// checksum covers no address, or for a UDP checksum of zero, which means none. A checksum that was wrong
        /// before the address changed stays wrong.
        /// </remarks>
        /// <param name="oldAddress">The address as it was, in network byte order.</param>
        /// <param name="newAddress">The address as it is now, of the same IP version.</param>
        /// <exception cref="ArgumentException">The addresses are not both 4 or both 16 bytes long.</exception>
        public void UpdatePayloadChecksum(ReadOnlySpan<byte> oldAddress, ReadOnlySpan<byte> newAddress)
        {
            if (oldAddress.Length != newAddress.Length || oldAddress.Length is not (4 or 16))
                throw new ArgumentException("The addresses must be of one IP version.", nameof(newAddress));

            // where the protocol keeps a checksum that covers the addresses
            var checksumOffset = ipPacket.Protocol switch {
                IpProtocol.Udp => 6,
                IpProtocol.Tcp => 16,
                IpProtocol.IcmpV6 => 2,
                _ => -1
            };

            // its place in this packet, which holds the payload from payloadOffset on
            var payloadOffset = GetPayloadOffset(ipPacket);
            var payload = ipPacket.Payload.Span;
            if (checksumOffset < 0 || payloadOffset < 0 || checksumOffset < payloadOffset ||
                payload.Length < checksumOffset - payloadOffset + 2)
                return;

            var checksumField = payload.Slice(checksumOffset - payloadOffset, 2);
            var checksum = BinaryPrimitives.ReadUInt16BigEndian(checksumField);
            var isUdp = ipPacket.Protocol == IpProtocol.Udp;
            if (isUdp && checksum == 0)
                return;

            // RFC 1624's HC' = ~(~HC + ~m + m'), as the checksum of those words: the old checksum's complement, the
            // old address's, and the new address
            Span<byte> words = stackalloc byte[2 + 16 + 16];
            words = words[..(2 + oldAddress.Length * 2)];
            BinaryPrimitives.WriteUInt16BigEndian(words, (ushort)~checksum);
            for (var i = 0; i < oldAddress.Length; i++)
                words[2 + i] = (byte)~oldAddress[i];

            newAddress.CopyTo(words[(2 + oldAddress.Length)..]);
            var newChecksum = ChecksumUtils.ComputeChecksum(words);

            // UDP sends a computed zero as all ones, as zero means no checksum
            if (isUdp && newChecksum == 0)
                newChecksum = 0xFFFF;

            BinaryPrimitives.WriteUInt16BigEndian(checksumField, newChecksum);
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

    // Where the packet's payload begins in its upper-layer packet, in bytes: at its start, but for a fragment after
    // its datagram's first, which holds a later piece. An IPv4 fragment's offset says where. An IPv6 one's counts
    // the extension headers before the upper-layer packet too, which only the first fragment holds, so there it
    // is not known: -1.
    private static int GetPayloadOffset(IpPacket ipPacket)
    {
        if (!ipPacket.IsFragment)
            return 0;

        if (ipPacket is IpV4Packet ipV4Packet)
            return ipV4Packet.FragmentOffset * 8;

        IpV6ExtensionHeaders.FindUpperLayer(ipPacket.Buffer.Span, out _, out _, out var fragmentOffset);
        return fragmentOffset == 0 ? 0 : -1;
    }

    // a fragment's payload is a piece of its upper-layer packet, which reads as one only by chance
    private static Memory<byte> GetWholePayload(IpPacket ipPacket)
    {
        return ipPacket.IsFragment
            ? throw new InvalidDataException($"A fragment holds only a piece of its {ipPacket.Protocol} packet.")
            : ipPacket.Payload;
    }
}
