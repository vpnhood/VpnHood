using System.Buffers;
using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.Packets;

/// <summary>
/// Provides methods that parse IP packets and build new ones: UDP, TCP, ICMP and the replies a router or a
/// host sends.
/// </summary>
/// <remarks>
/// A built packet owns memory rented from a <see cref="MemoryPool{T}"/>, <see cref="MemoryPool{T}.Shared"/> unless
/// another is given: dispose it to return the memory. Its time to live is 64. A method with an
/// <c>updateChecksum</c> parameter computes the checksums when it is <see langword="true"/>; the others compute
/// none, so call <see cref="IpPacketExtensions.UpdateAllChecksums(IpPacket)"/> before sending.
/// </remarks>
/// <example>
/// <code snippet="Snippet:PacketsBuildUdp" language="csharp">
/// using var ipPacket = PacketBuilder.BuildUdp(
///     new IPEndPoint(IPAddress.Parse("10.0.0.2"), 5353),
///     new IPEndPoint(IPAddress.Parse("10.0.0.1"), 53),
///     "hello"u8);
///
/// ipPacket.UpdateAllChecksums(); // BuildUdp computes no checksum itself
/// var bytes = ipPacket.Buffer; // the packet's own bytes, ready for a TUN device
/// </code>
/// </example>
public static class PacketBuilder
{
    /// <summary>
    /// Parses the IP packet at the start of some bytes into a copy that the returned packet owns.
    /// </summary>
    /// <param name="buffer">The bytes, which hold the packet from the first byte and may be longer.</param>
    /// <param name="memoryPool">The pool to rent the copy's memory from, or <see langword="null"/> for <see cref="MemoryPool{T}.Shared"/>.</param>
    /// <returns>An <see cref="IpV4Packet"/> or <see cref="IpV6Packet"/>; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentException">The bytes are no valid IP packet.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="buffer"/> is shorter than the packet's length field says.</exception>
    /// <exception cref="Exception">The version is neither 4 nor 6.</exception>
    /// <exception cref="IndexOutOfRangeException"><paramref name="buffer"/> is empty.</exception>
    /// <example>
    /// <code snippet="Snippet:PacketsParse" language="csharp">
    /// // Parse copies the received bytes into pooled memory; PacketBuilder.Attach would read them in place
    /// using var ipPacket = PacketBuilder.Parse(received);
    /// Console.WriteLine($"{ipPacket.SourceAddress} -&gt; {ipPacket.DestinationAddress}, {ipPacket.Protocol}");
    ///
    /// if (ipPacket.Protocol == IpProtocol.Udp &amp;&amp; !ipPacket.IsFragment) {
    ///     var udp = ipPacket.ExtractUdp(); // a view over the packet's bytes, not a copy
    ///     Console.WriteLine($"UDP {udp.SourcePort} -&gt; {udp.DestinationPort}, {udp.Payload.Length} bytes, " +
    ///                       $"checksum valid: {udp.IsChecksumValid(ipPacket)}");
    /// }
    /// </code>
    /// </example>
    public static IpPacket Parse(ReadOnlySpan<byte> buffer, MemoryPool<byte>? memoryPool = null)
    {
        // adjust buffer length by parsing the packet length
        var packetLength = PacketUtil.ReadPacketLength(buffer);
        buffer = buffer[..packetLength];

        // copy buffer to memory pool
        memoryPool ??= MemoryPool<byte>.Shared;
        var memoryOwner = memoryPool.Rent(buffer.Length);
        buffer.CopyTo(memoryOwner.Memory.Span);

        return Attach(memoryOwner);
    }

    /// <summary>
    /// Parses the IP packet at the start of a buffer, without copying it: the packet reads and writes the buffer.
    /// </summary>
    /// <param name="buffer">The buffer, which holds the packet from its first byte and may be longer. It stays the caller's.</param>
    /// <returns>An <see cref="IpV4Packet"/> or <see cref="IpV6Packet"/> over the buffer.</returns>
    /// <exception cref="NotSupportedException">The version is neither 4 nor 6.</exception>
    /// <exception cref="ArgumentException">The bytes are no valid IP packet.</exception>
    /// <exception cref="IndexOutOfRangeException">The buffer is empty.</exception>
    public static IpPacket Attach(Memory<byte> buffer)
    {
        // find packet length by reading the first byte
        var version = IpPacket.GetPacketVersion(buffer.Span);
        return version switch {
            IpVersion.IPv4 => new IpV4Packet(buffer),
            IpVersion.IPv6 => new IpV6Packet(buffer),
            _ => throw new NotSupportedException($"IP version {version} not supported.")
        };
    }

    /// <summary>
    /// Parses the IP packet at the start of rented memory, without copying it, and gives the packet that memory.
    /// </summary>
    /// <param name="memoryOwner">
    /// The memory, which holds the packet from its first byte and may be longer. The packet disposes it; when
    /// parsing fails, it stays the caller's.
    /// </param>
    /// <returns>An <see cref="IpV4Packet"/> or <see cref="IpV6Packet"/>; dispose it to return the memory.</returns>
    /// <exception cref="NotSupportedException">The version is neither 4 nor 6.</exception>
    /// <exception cref="ArgumentException">The bytes are no valid IP packet.</exception>
    /// <exception cref="IndexOutOfRangeException">The buffer is empty.</exception>
    public static IpPacket Attach(IMemoryOwner<byte> memoryOwner)
    {
        var version = IpPacket.GetPacketVersion(memoryOwner.Memory.Span);
        return version switch {
            IpVersion.IPv4 => new IpV4Packet(memoryOwner),
            IpVersion.IPv6 => new IpV6Packet(memoryOwner),
            _ => throw new NotSupportedException($"IP version {version} not supported.")
        };
    }

    /// <summary>
    /// Builds an IP packet with an empty payload of a given length, for the caller to fill.
    /// </summary>
    /// <param name="sourceAddress">The source address.</param>
    /// <param name="destinationAddress">The destination address, of the same IP version.</param>
    /// <param name="protocol">The protocol of the payload.</param>
    /// <param name="payloadLength">The payload's length in bytes; at most 65515 for IPv4 and 65535 for IPv6.</param>
    /// <param name="memoryPool">The pool to rent the packet's memory from, or <see langword="null"/> for <see cref="MemoryPool{T}.Shared"/>.</param>
    /// <returns>The packet, with its payload zero; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentException">
    /// The addresses are of different IP versions, or an IPv4 packet would exceed 65535 bytes.
    /// </exception>
    /// <exception cref="NotSupportedException">The addresses are neither IPv4 nor IPv6.</exception>
    public static IpPacket BuildIp(IPAddress sourceAddress, IPAddress destinationAddress,
        IpProtocol protocol, int payloadLength, MemoryPool<byte>? memoryPool = null)
    {
        // validate address length
        if (sourceAddress.AddressFamily != destinationAddress.AddressFamily)
            throw new ArgumentException("SourceAddress and DestinationAddress must have a same ip version.");

        // validate payload length
        memoryPool ??= MemoryPool<byte>.Shared;

        // create IP packet
        IpPacket ipPacket = sourceAddress.AddressFamily switch {
            AddressFamily.InterNetwork => new IpV4Packet(memoryPool.Rent(20 + payloadLength), 20 + payloadLength,
                protocol, 0),
            AddressFamily.InterNetworkV6 => new IpV6Packet(memoryPool.Rent(40 + payloadLength), 40 + payloadLength,
                protocol),
            _ => throw new NotSupportedException($"{sourceAddress.AddressFamily} not supported.")
        };

        ipPacket.SourceAddress = sourceAddress;
        ipPacket.DestinationAddress = destinationAddress;
        ipPacket.TimeToLive = 64;
        return ipPacket;
    }

    /// <summary>
    /// Builds an IP packet with an empty payload of a given length, for the caller to fill, from address values.
    /// </summary>
    /// <param name="sourceAddress">The source address.</param>
    /// <param name="destinationAddress">The destination address, of the same IP version.</param>
    /// <param name="protocol">The protocol of the payload.</param>
    /// <param name="payloadLength">The payload's length in bytes; at most 65515 for IPv4 and 65535 for IPv6.</param>
    /// <param name="memoryPool">The pool to rent the packet's memory from, or <see langword="null"/> for <see cref="MemoryPool{T}.Shared"/>.</param>
    /// <returns>The packet, with its payload zero; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentException">
    /// The addresses are of different IP versions, or an IPv4 packet would exceed 65535 bytes.
    /// </exception>
    /// <exception cref="NotSupportedException">The addresses hold no address.</exception>
    public static IpPacket BuildIp(IpAddressValue sourceAddress, IpAddressValue destinationAddress,
        IpProtocol protocol, int payloadLength, MemoryPool<byte>? memoryPool = null)
    {
        if (sourceAddress.IsV4 != destinationAddress.IsV4 || sourceAddress.IsV6 != destinationAddress.IsV6)
            throw new ArgumentException("SourceAddress and DestinationAddress must have a same ip version.");

        memoryPool ??= MemoryPool<byte>.Shared;
        IpPacket ipPacket = sourceAddress switch {
            { IsV4: true } => new IpV4Packet(memoryPool.Rent(20 + payloadLength), 20 + payloadLength, protocol, 0),
            { IsV6: true } => new IpV6Packet(memoryPool.Rent(40 + payloadLength), 40 + payloadLength, protocol),
            _ => throw new NotSupportedException("The addresses hold no IP address.")
        };

        ipPacket.SourceAddressValue = sourceAddress;
        ipPacket.DestinationAddressValue = destinationAddress;
        ipPacket.TimeToLive = 64;
        return ipPacket;
    }

    /// <summary>
    /// Builds an IP packet with an empty payload of a given length, for the caller to fill, from raw address bytes.
    /// </summary>
    /// <param name="sourceAddress">The source address: 4 bytes for IPv4, 16 for IPv6.</param>
    /// <param name="destinationAddress">The destination address, of the same length.</param>
    /// <param name="protocol">The protocol of the payload.</param>
    /// <param name="payloadLength">The payload's length in bytes; at most 65515 for IPv4 and 65535 for IPv6.</param>
    /// <param name="memoryPool">The pool to rent the packet's memory from, or <see langword="null"/> for <see cref="MemoryPool{T}.Shared"/>.</param>
    /// <returns>The packet, with its payload zero; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentException">
    /// The addresses differ in length, or an IPv4 packet would exceed 65535 bytes.
    /// </exception>
    /// <exception cref="NotSupportedException">The addresses are neither 4 nor 16 bytes long.</exception>
    public static IpPacket BuildIp(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress,
        IpProtocol protocol, int payloadLength, MemoryPool<byte>? memoryPool = null)
    {
        // validate address length
        if (sourceAddress.Length != destinationAddress.Length)
            throw new ArgumentException("SourceAddress and DestinationAddress must have a same ip version.");

        // validate payload length
        memoryPool ??= MemoryPool<byte>.Shared;

        // create IP packet
        IpPacket ipPacket = sourceAddress.Length switch {
            4 => new IpV4Packet(memoryPool.Rent(20 + payloadLength), 20 + payloadLength, protocol, 0),
            16 => new IpV6Packet(memoryPool.Rent(40 + payloadLength), 40 + payloadLength, protocol),
            _ => throw new NotSupportedException($"IP version {sourceAddress.Length} not supported.")
        };

        ipPacket.SourceAddressSpan = sourceAddress;
        ipPacket.DestinationAddressSpan = destinationAddress;
        ipPacket.TimeToLive = 64;
        return ipPacket;
    }

    /// <summary>
    /// Builds an IP packet that carries a UDP datagram.
    /// </summary>
    /// <remarks>
    /// No checksum is computed: call <see cref="IpPacketExtensions.UpdateAllChecksums(IpPacket)"/> before sending.
    /// </remarks>
    /// <param name="sourceEndPoint">The source address and port.</param>
    /// <param name="destinationEndPoint">The destination address and port, of the same IP version.</param>
    /// <param name="payload">The datagram's payload, which is copied in.</param>
    /// <returns>The packet; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentException">The addresses are of different IP versions, or the packet would be too large.</exception>
    /// <exception cref="NotSupportedException">The addresses are neither IPv4 nor IPv6.</exception>
    public static IpPacket BuildUdp(IPEndPoint sourceEndPoint, IPEndPoint destinationEndPoint,
        ReadOnlySpan<byte> payload)
    {
        return BuildUdp(
            sourceEndPoint.Address, destinationEndPoint.Address,
            sourceEndPoint.Port, destinationEndPoint.Port,
            payload);
    }

    /// <summary>
    /// Builds an IP packet that carries a UDP datagram.
    /// </summary>
    /// <remarks>
    /// No checksum is computed: call <see cref="IpPacketExtensions.UpdateAllChecksums(IpPacket)"/> before sending.
    /// </remarks>
    /// <param name="sourceAddress">The source address.</param>
    /// <param name="destinationAddress">The destination address, of the same IP version.</param>
    /// <param name="sourcePort">The source port, from 0 to 65535.</param>
    /// <param name="destinationPort">The destination port, from 0 to 65535.</param>
    /// <param name="payload">The datagram's payload, which is copied in.</param>
    /// <returns>The packet; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentException">The addresses are of different IP versions, or the packet would be too large.</exception>
    /// <exception cref="NotSupportedException">The addresses are neither IPv4 nor IPv6.</exception>
    public static IpPacket BuildUdp(IPAddress sourceAddress, IPAddress destinationAddress,
        int sourcePort, int destinationPort, ReadOnlySpan<byte> payload)
    {
        var ipPacket = BuildIp(sourceAddress, destinationAddress, IpProtocol.Udp, 8 + payload.Length);
        var udpPacket = ipPacket.BuildUdp();
        udpPacket.SourcePort = (ushort)sourcePort;
        udpPacket.DestinationPort = (ushort)destinationPort;
        payload.CopyTo(udpPacket.Payload.Span);
        return ipPacket;
    }

    /// <summary>
    /// Builds an IP packet that carries a TCP segment, with its flags, numbers and window zero.
    /// </summary>
    /// <remarks>
    /// No checksum is computed: call <see cref="IpPacketExtensions.UpdateAllChecksums(IpPacket)"/> before sending.
    /// </remarks>
    /// <param name="sourceEndPoint">The source address and port.</param>
    /// <param name="destinationEndPoint">The destination address and port, of the same IP version.</param>
    /// <param name="options">The TCP options, a multiple of 4 bytes up to 40, which are copied in.</param>
    /// <param name="payload">The segment's payload, which is copied in.</param>
    /// <returns>The packet; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentException">The addresses are of different IP versions, or the packet would be too large.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/> is longer than 40 bytes or not a multiple of 4.</exception>
    /// <exception cref="NotSupportedException">The addresses are neither IPv4 nor IPv6.</exception>
    public static IpPacket BuildTcp(IpEndPointValue sourceEndPoint, IpEndPointValue destinationEndPoint,
        ReadOnlySpan<byte> options, ReadOnlySpan<byte> payload)
    {
        var ipPacket = BuildIp(sourceEndPoint.AddressValue, destinationEndPoint.AddressValue, IpProtocol.Tcp,
            20 + options.Length + payload.Length);
        return FillTcp(ipPacket, sourceEndPoint.Port, destinationEndPoint.Port, options, payload);
    }

    /// <summary>
    /// Builds an IP packet that carries a TCP segment, with its flags, numbers and window zero.
    /// </summary>
    /// <remarks>
    /// No checksum is computed: call <see cref="IpPacketExtensions.UpdateAllChecksums(IpPacket)"/> before sending.
    /// </remarks>
    /// <param name="sourceEndPoint">The source address and port.</param>
    /// <param name="destinationEndPoint">The destination address and port, of the same IP version.</param>
    /// <param name="options">The TCP options, a multiple of 4 bytes up to 40, which are copied in.</param>
    /// <param name="payload">The segment's payload, which is copied in.</param>
    /// <returns>The packet; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentException">The addresses are of different IP versions, or the packet would be too large.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/> is longer than 40 bytes or not a multiple of 4.</exception>
    /// <exception cref="NotSupportedException">The addresses are neither IPv4 nor IPv6.</exception>
    public static IpPacket BuildTcp(IPEndPoint sourceEndPoint, IPEndPoint destinationEndPoint,
        ReadOnlySpan<byte> options, ReadOnlySpan<byte> payload)
    {
        return BuildTcp(
            sourceEndPoint.Address, destinationEndPoint.Address,
            sourceEndPoint.Port, destinationEndPoint.Port,
            options, payload);
    }

    /// <summary>
    /// Builds an IP packet that carries a TCP segment, with its flags, numbers and window zero.
    /// </summary>
    /// <remarks>
    /// No checksum is computed: call <see cref="IpPacketExtensions.UpdateAllChecksums(IpPacket)"/> before sending.
    /// </remarks>
    /// <param name="sourceAddress">The source address.</param>
    /// <param name="destinationAddress">The destination address, of the same IP version.</param>
    /// <param name="sourcePort">The source port, from 0 to 65535.</param>
    /// <param name="destinationPort">The destination port, from 0 to 65535.</param>
    /// <param name="options">The TCP options, a multiple of 4 bytes up to 40, which are copied in.</param>
    /// <param name="payload">The segment's payload, which is copied in.</param>
    /// <returns>The packet; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentException">The addresses are of different IP versions, or the packet would be too large.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="options"/> is longer than 40 bytes or not a multiple of 4.</exception>
    /// <exception cref="NotSupportedException">The addresses are neither IPv4 nor IPv6.</exception>
    public static IpPacket BuildTcp(IPAddress sourceAddress, IPAddress destinationAddress,
        int sourcePort, int destinationPort, ReadOnlySpan<byte> options, ReadOnlySpan<byte> payload)
    {
        var ipPacket = BuildIp(sourceAddress, destinationAddress, IpProtocol.Tcp, 20 + options.Length + payload.Length);
        return FillTcp(ipPacket, sourcePort, destinationPort, options, payload);
    }

    /// <summary>
    /// Builds an IPv4 packet that carries an ICMPv4 message, with its type, code and message-specific field zero.
    /// </summary>
    /// <remarks>
    /// Type zero is an echo reply: set <see cref="IcmpV4Packet.Type"/> through
    /// <see cref="IpPacketExtensions.ExtractIcmpV4(IpPacket)"/>. No checksum is computed: call
    /// <see cref="IpPacketExtensions.UpdateAllChecksums(IpPacket)"/> before sending.
    /// </remarks>
    /// <param name="sourceAddress">The source address, IPv4.</param>
    /// <param name="destinationAddress">The destination address, IPv4.</param>
    /// <param name="payload">The message's payload, which is copied in.</param>
    /// <returns>The packet; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentException">The addresses are of different IP versions, or the packet would be too large.</exception>
    public static IpPacket BuildIcmpV4(IPAddress sourceAddress, IPAddress destinationAddress,
        ReadOnlySpan<byte> payload)
    {
        var ipPacket = BuildIp(sourceAddress, destinationAddress, IpProtocol.IcmpV4, 8 + payload.Length);
        payload.CopyTo(ipPacket.BuildIcmpV4().Payload.Span);
        return ipPacket;
    }

    /// <summary>
    /// Builds an IPv6 packet that carries an ICMPv6 message, with its type, code and message-specific field zero.
    /// </summary>
    /// <remarks>
    /// Set <see cref="IcmpV6Packet.Type"/> through <see cref="IpPacketExtensions.ExtractIcmpV6(IpPacket)"/>. No
    /// checksum is computed: call <see cref="IpPacketExtensions.UpdateAllChecksums(IpPacket)"/> before sending.
    /// </remarks>
    /// <param name="sourceAddress">The source address, IPv6.</param>
    /// <param name="destinationAddress">The destination address, IPv6.</param>
    /// <param name="payload">The message's payload, which is copied in.</param>
    /// <returns>The packet; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentException">The addresses are of different IP versions.</exception>
    public static IpPacket BuildIcmpV6(IPAddress sourceAddress, IPAddress destinationAddress,
        ReadOnlySpan<byte> payload)
    {
        var ipPacket = BuildIp(sourceAddress, destinationAddress, IpProtocol.IcmpV6, 8 + payload.Length);
        payload.CopyTo(ipPacket.BuildIcmpV6().Payload.Span);
        return ipPacket;
    }

    /// <summary>
    /// Builds an ICMPv4 or ICMPv6 echo request, a ping, by the addresses' IP version.
    /// </summary>
    /// <param name="sourceAddress">The source address.</param>
    /// <param name="destinationAddress">The destination address, of the same IP version.</param>
    /// <param name="payload">The echo's data, which is copied in.</param>
    /// <param name="identifier">The echo's identifier.</param>
    /// <param name="sequenceNumber">The echo's sequence number.</param>
    /// <param name="updateChecksum"><see langword="true"/> to compute the checksums; <see langword="false"/> to leave them zero.</param>
    /// <returns>The packet; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentException">The addresses are of different IP versions.</exception>
    /// <exception cref="NotSupportedException">The addresses are neither IPv4 nor IPv6.</exception>
    public static IpPacket BuildIcmpEchoRequest(IPAddress sourceAddress, IPAddress destinationAddress,
        ReadOnlySpan<byte> payload, ushort identifier = 0, ushort sequenceNumber = 0, bool updateChecksum = true)
    {
        if (sourceAddress.AddressFamily != destinationAddress.AddressFamily)
            throw new ArgumentException("SourceAddress and DestinationAddress must have a same address family.");

        return sourceAddress.AddressFamily switch {
            AddressFamily.InterNetwork => BuildIcmpV4EchoRequest(sourceAddress, destinationAddress, payload, identifier,
                sequenceNumber, updateChecksum),
            AddressFamily.InterNetworkV6 => BuildIcmpV6EchoRequest(sourceAddress, destinationAddress, payload,
                identifier, sequenceNumber, updateChecksum),
            _ => throw new NotSupportedException($"{sourceAddress.AddressFamily} not supported.")
        };
    }

    /// <summary>
    /// Builds an ICMPv4 echo request, a ping.
    /// </summary>
    /// <param name="sourceAddress">The source address, IPv4.</param>
    /// <param name="destinationAddress">The destination address, IPv4.</param>
    /// <param name="payload">The echo's data, which is copied in.</param>
    /// <param name="identifier">The echo's identifier.</param>
    /// <param name="sequenceNumber">The echo's sequence number.</param>
    /// <param name="updateChecksum"><see langword="true"/> to compute the checksums; <see langword="false"/> to leave them zero.</param>
    /// <returns>The packet; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentException">An address is not IPv4.</exception>
    public static IpPacket BuildIcmpV4EchoRequest(IPAddress sourceAddress, IPAddress destinationAddress,
        ReadOnlySpan<byte> payload, ushort identifier = 0, ushort sequenceNumber = 0, bool updateChecksum = true)
    {
        if (!sourceAddress.IsV4 || !destinationAddress.IsV4)
            throw new ArgumentException("SourceAddress and DestinationAddress must be IPv4 addresses.");

        // ICMP echo request
        var ipPacket = BuildIcmpV4(sourceAddress, destinationAddress, payload);
        var icmp = ipPacket.ExtractIcmpV4();
        icmp.Type = IcmpV4Type.EchoRequest;
        icmp.Code = 0;
        icmp.SequenceNumber = sequenceNumber;
        icmp.Identifier = identifier;
        if (updateChecksum)
            ipPacket.UpdateAllChecksums();

        return ipPacket;
    }

    /// <summary>
    /// Builds an ICMPv6 echo request, a ping.
    /// </summary>
    /// <param name="sourceAddress">The source address, IPv6.</param>
    /// <param name="destinationAddress">The destination address, IPv6.</param>
    /// <param name="payload">The echo's data, which is copied in.</param>
    /// <param name="identifier">The echo's identifier.</param>
    /// <param name="sequenceNumber">The echo's sequence number.</param>
    /// <param name="updateChecksum"><see langword="true"/> to compute the checksums; <see langword="false"/> to leave them zero.</param>
    /// <returns>The packet; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentException">An address is not IPv6.</exception>
    public static IpPacket BuildIcmpV6EchoRequest(IPAddress sourceAddress, IPAddress destinationAddress,
        ReadOnlySpan<byte> payload, ushort identifier = 0, ushort sequenceNumber = 0, bool updateChecksum = true)
    {
        if (!sourceAddress.IsV6 || !destinationAddress.IsV6)
            throw new ArgumentException("SourceAddress and DestinationAddress must be IPv6 addresses.");

        // ICMP echo request
        var ipPacket = BuildIcmpV6(sourceAddress, destinationAddress, payload);
        var icmp = ipPacket.ExtractIcmpV6();
        icmp.Type = IcmpV6Type.EchoRequest;
        icmp.Code = 0;
        icmp.SequenceNumber = sequenceNumber;
        icmp.Identifier = identifier;
        if (updateChecksum)
            ipPacket.UpdateAllChecksums();

        return ipPacket;
    }

    /// <summary>
    /// Builds an ICMPv4 or ICMPv6 echo reply, the answer to a ping, by the addresses' IP version.
    /// </summary>
    /// <param name="sourceAddress">The source address.</param>
    /// <param name="destinationAddress">The destination address, of the same IP version.</param>
    /// <param name="payload">The echo's data, which is copied in: the request's data, for a reply to it.</param>
    /// <param name="identifier">The echo's identifier: the request's, for a reply to it.</param>
    /// <param name="sequenceNumber">The echo's sequence number: the request's, for a reply to it.</param>
    /// <param name="updateChecksum"><see langword="true"/> to compute the checksums; <see langword="false"/> to leave them zero.</param>
    /// <returns>The packet; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentException">The addresses are of different IP versions.</exception>
    /// <exception cref="NotSupportedException">The addresses are neither IPv4 nor IPv6.</exception>
    public static IpPacket BuildIcmpEchoReply(
        IPAddress sourceAddress, IPAddress destinationAddress,
        ReadOnlySpan<byte> payload, ushort identifier = 0, ushort sequenceNumber = 0, bool updateChecksum = true)
    {
        if (sourceAddress.AddressFamily != destinationAddress.AddressFamily)
            throw new ArgumentException("SourceAddress and DestinationAddress must have a same ip version.");

        return sourceAddress.AddressFamily switch {
            AddressFamily.InterNetwork => BuildIcmpV4EchoReply(sourceAddress, destinationAddress, payload, identifier,
                sequenceNumber, updateChecksum),
            AddressFamily.InterNetworkV6 => BuildIcmpV6EchoReply(sourceAddress, destinationAddress, payload, identifier,
                sequenceNumber, updateChecksum),
            _ => throw new NotSupportedException($"{sourceAddress.AddressFamily} not supported.")
        };
    }

    /// <summary>
    /// Builds an ICMPv4 echo reply, the answer to a ping.
    /// </summary>
    /// <param name="sourceAddress">The source address, IPv4.</param>
    /// <param name="destinationAddress">The destination address, IPv4.</param>
    /// <param name="payload">The echo's data, which is copied in: the request's data, for a reply to it.</param>
    /// <param name="identifier">The echo's identifier: the request's, for a reply to it.</param>
    /// <param name="sequenceNumber">The echo's sequence number: the request's, for a reply to it.</param>
    /// <param name="updateChecksum"><see langword="true"/> to compute the checksums; <see langword="false"/> to leave them zero.</param>
    /// <returns>The packet; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentException">An address is not IPv4.</exception>
    public static IpPacket BuildIcmpV4EchoReply(
        IPAddress sourceAddress, IPAddress destinationAddress,
        ReadOnlySpan<byte> payload, ushort identifier = 0, ushort sequenceNumber = 0, bool updateChecksum = true)
    {
        if (!sourceAddress.IsV4 || !destinationAddress.IsV4)
            throw new ArgumentException("SourceAddress and DestinationAddress must be IPv4 addresses.");

        var ipPacket = BuildIcmpV4(sourceAddress, destinationAddress, payload);
        var icmp = ipPacket.ExtractIcmpV4();
        icmp.Type = IcmpV4Type.EchoReply;
        icmp.Code = 0;
        icmp.SequenceNumber = sequenceNumber;
        icmp.Identifier = identifier;
        if (updateChecksum)
            ipPacket.UpdateAllChecksums();

        return ipPacket;
    }

    /// <summary>
    /// Builds an ICMPv6 echo reply, the answer to a ping.
    /// </summary>
    /// <param name="sourceAddress">The source address, IPv6.</param>
    /// <param name="destinationAddress">The destination address, IPv6.</param>
    /// <param name="payload">The echo's data, which is copied in: the request's data, for a reply to it.</param>
    /// <param name="identifier">The echo's identifier: the request's, for a reply to it.</param>
    /// <param name="sequenceNumber">The echo's sequence number: the request's, for a reply to it.</param>
    /// <param name="updateChecksum"><see langword="true"/> to compute the checksums; <see langword="false"/> to leave them zero.</param>
    /// <returns>The packet; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentException">An address is not IPv6.</exception>
    public static IpPacket BuildIcmpV6EchoReply(
        IPAddress sourceAddress, IPAddress destinationAddress,
        ReadOnlySpan<byte> payload, ushort identifier = 0, ushort sequenceNumber = 0, bool updateChecksum = true)
    {
        if (!sourceAddress.IsV6 || !destinationAddress.IsV6)
            throw new ArgumentException("SourceAddress and DestinationAddress must be IPv6 addresses.");

        var ipPacket = BuildIcmpV6(sourceAddress, destinationAddress, payload);
        var icmp = ipPacket.ExtractIcmpV6();
        icmp.Type = IcmpV6Type.EchoReply;
        icmp.Code = 0;
        icmp.SequenceNumber = sequenceNumber;
        icmp.Identifier = identifier;
        if (updateChecksum)
            ipPacket.UpdateAllChecksums();

        return ipPacket;
    }

    /// <summary>
    /// Builds the TCP reset (RST) that refuses a TCP packet, from its destination back to its source.
    /// </summary>
    /// <remarks>
    /// A reset for a SYN without ACK acknowledges it: sequence number 0, acknowledgment number the SYN's sequence
    /// number plus 1. For any other packet, the reset's sequence number is the packet's acknowledgment number,
    /// with no ACK.
    /// </remarks>
    /// <param name="ipPacket">The TCP packet to refuse.</param>
    /// <param name="updateChecksum"><see langword="true"/> to compute the checksums; <see langword="false"/> to leave them zero.</param>
    /// <returns>The reset packet; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ipPacket"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="ipPacket"/> does not carry TCP, or its TCP segment is invalid.</exception>
    /// <exception cref="InvalidDataException"><paramref name="ipPacket"/> is a fragment.</exception>
    public static IpPacket BuildTcpResetReply(IpPacket ipPacket, bool updateChecksum = true)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));
        if (ipPacket.Protocol != IpProtocol.Tcp)
            throw new ArgumentException("packet is not TCP!", nameof(ipPacket));

        var tcpPacketOrg = ipPacket.ExtractTcp();

        var resetIpPacket = BuildTcp(
            ipPacket.DestinationAddress, ipPacket.SourceAddress,
            tcpPacketOrg.DestinationPort, tcpPacketOrg.SourcePort, null, null);

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

    /// <summary>
    /// Builds an IP packet that carries a DNS query for a name's IPv4 addresses (type A, class IN, recursion
    /// desired) over UDP.
    /// </summary>
    /// <remarks>
    /// No checksum is computed: call <see cref="IpPacketExtensions.UpdateAllChecksums(IpPacket)"/> before sending.
    /// </remarks>
    /// <param name="sourceAddress">The source address.</param>
    /// <param name="destinationAddress">The DNS server's address, of the same IP version.</param>
    /// <param name="sourcePort">The source port.</param>
    /// <param name="destinationPort">The DNS server's port, usually 53.</param>
    /// <param name="host">The name to look up.</param>
    /// <param name="queryId">The query's ID, or <see langword="null"/> for a random one.</param>
    /// <returns>The packet; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentException">The addresses are of different IP versions.</exception>
    /// <exception cref="NotSupportedException">The addresses are neither IPv4 nor IPv6.</exception>
    public static IpPacket BuildDns(IPAddress sourceAddress, IPAddress destinationAddress,
        int sourcePort, int destinationPort, string host, ushort? queryId = null)
    {
        queryId ??= (ushort)new Random().Next(ushort.MaxValue);
        var query = DnsUtils.BuildDnsQuery(queryId.Value, host);
        var dnsPacket = BuildUdp(
            sourceAddress: sourceAddress,
            destinationAddress: destinationAddress,
            sourcePort: sourcePort,
            destinationPort: destinationPort,
            query);

        return dnsPacket;
    }

    /// <summary>
    /// Builds an IP packet that carries a DNS query for a name's IPv4 addresses (type A, class IN, recursion
    /// desired) over UDP.
    /// </summary>
    /// <remarks>
    /// No checksum is computed: call <see cref="IpPacketExtensions.UpdateAllChecksums(IpPacket)"/> before sending.
    /// </remarks>
    /// <param name="sourceEndPoint">The source address and port.</param>
    /// <param name="destinationEndPoint">The DNS server's address and port, of the same IP version.</param>
    /// <param name="host">The name to look up.</param>
    /// <param name="queryId">The query's ID, or <see langword="null"/> for a random one.</param>
    /// <returns>The packet; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentException">The addresses are of different IP versions.</exception>
    /// <exception cref="NotSupportedException">The addresses are neither IPv4 nor IPv6.</exception>
    public static IpPacket BuildDns(IPEndPoint sourceEndPoint, IPEndPoint destinationEndPoint,
        string host, ushort? queryId = null)
    {
        return BuildDns(
            sourceAddress: sourceEndPoint.Address,
            destinationAddress: destinationEndPoint.Address,
            sourcePort: sourceEndPoint.Port,
            destinationPort: destinationEndPoint.Port,
            host,
            queryId);
    }

    /// <summary>
    /// Builds the ICMP error that tells a packet's sender its destination is unreachable: ICMPv4 "host
    /// unreachable", or ICMPv6 "address unreachable".
    /// </summary>
    /// <param name="ipPacket">The packet that cannot be delivered. The error goes from its destination to its source and carries its start.</param>
    /// <param name="updateChecksum"><see langword="true"/> to compute the checksums; <see langword="false"/> to leave them zero.</param>
    /// <returns>The error packet; dispose it to return its memory.</returns>
    /// <exception cref="ObjectDisposedException"><paramref name="ipPacket"/> is disposed.</exception>
    public static IpPacket BuildIcmpUnreachableReply(IpPacket ipPacket, bool updateChecksum = true)
    {
        return ipPacket.Version == IpVersion.IPv6
            ? BuildIcmpV6Error(ipPacket, IcmpV6Type.DestinationUnreachable, IcmpV6Code.AddressUnreachable, 0,
                updateChecksum)
            : BuildIcmpV4Error(ipPacket, IcmpV4Type.DestinationUnreachable, IcmpV4Code.HostUnreachable, 0,
                updateChecksum);
    }

    /// <summary>
    /// Builds the ICMP error that tells a packet's sender its destination port is unreachable.
    /// </summary>
    /// <param name="ipPacket">The packet that cannot be delivered. The error goes from its destination to its source and carries its start.</param>
    /// <param name="updateChecksum"><see langword="true"/> to compute the checksums; <see langword="false"/> to leave them zero.</param>
    /// <returns>The error packet; dispose it to return its memory.</returns>
    /// <exception cref="ObjectDisposedException"><paramref name="ipPacket"/> is disposed.</exception>
    public static IpPacket BuildIcmpUnreachablePortReply(IpPacket ipPacket, bool updateChecksum = true)
    {
        return ipPacket.Version == IpVersion.IPv6
            ? BuildIcmpV6Error(ipPacket, IcmpV6Type.DestinationUnreachable, IcmpV6Code.PortUnreachable, 0,
                updateChecksum)
            : BuildIcmpV4Error(ipPacket, IcmpV4Type.DestinationUnreachable, IcmpV4Code.PortUnreachable, 0,
                updateChecksum);
    }

    /// <summary>
    /// Builds the ICMP error that tells a packet's sender it is too big for the next link: ICMPv6 "packet too
    /// big", or ICMPv4 "fragmentation needed".
    /// </summary>
    /// <param name="ipPacket">The packet that is too big. The error goes from its destination to its source and carries its start.</param>
    /// <param name="mtu">The next link's MTU in bytes, which the error carries; ICMPv4 carries its low 16 bits.</param>
    /// <param name="updateChecksum"><see langword="true"/> to compute the checksums; <see langword="false"/> to leave them zero.</param>
    /// <returns>The error packet; dispose it to return its memory.</returns>
    /// <exception cref="ObjectDisposedException"><paramref name="ipPacket"/> is disposed.</exception>
    public static IpPacket BuildIcmpPacketTooBigReply(IpPacket ipPacket, int mtu, bool updateChecksum = true)
    {
        return ipPacket.Version == IpVersion.IPv6
            ? BuildIcmpV6Error(ipPacket, IcmpV6Type.PacketTooBig, IcmpV6Code.PacketTooBig,
                (ushort)mtu, updateChecksum)
            : BuildIcmpV4Error(ipPacket, IcmpV4Type.DestinationUnreachable, IcmpV4Code.FragmentationNeeded,
                (ushort)(mtu & 0xFFFF), updateChecksum);
    }

    /// <summary>
    /// Builds an ICMPv4 error about a packet, from its destination back to its source.
    /// </summary>
    /// <remarks>
    /// The error carries the packet's IP header and up to the first 8 bytes of its payload, as RFC 792 asks.
    /// </remarks>
    /// <param name="ipPacket">The IPv4 packet the error is about.</param>
    /// <param name="icmpV4Type">The error's type.</param>
    /// <param name="code">The error's code.</param>
    /// <param name="messageSpecific">The 4 bytes after the checksum, such as the next-hop MTU in the low 16 bits.</param>
    /// <param name="updateChecksum"><see langword="true"/> to compute the checksums; <see langword="false"/> to leave them zero.</param>
    /// <returns>The error packet; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ipPacket"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="ipPacket"/> is disposed.</exception>
    public static IpPacket BuildIcmpV4Error(IpPacket ipPacket, IcmpV4Type icmpV4Type, IcmpV4Code code,
        uint messageSpecific, bool updateChecksum)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

        // Copy original IP header (20 bytes) + 8 bytes of payload into ICMP payload
        var toCopy = Math.Min(ipPacket.Header.Length + 8, ipPacket.Buffer.Length);
        var icmpIpPacket = BuildIcmpV4(ipPacket.DestinationAddress, ipPacket.SourceAddress,
            ipPacket.Buffer.Span[..toCopy]);

        var icmpPacket = icmpIpPacket.ExtractIcmpV4();
        icmpPacket.Type = icmpV4Type;
        icmpPacket.Code = (byte)code;
        icmpPacket.MessageSpecific = messageSpecific;


        if (updateChecksum)
            icmpIpPacket.UpdateAllChecksums();

        return icmpIpPacket;
    }

    /// <summary>
    /// Builds an ICMPv6 error about a packet, from its destination back to its source.
    /// </summary>
    /// <remarks>
    /// The error carries the packet's first 1240 bytes, or the whole packet when it is shorter.
    /// </remarks>
    /// <param name="ipPacket">The IPv6 packet the error is about.</param>
    /// <param name="icmpV6Type">The error's type.</param>
    /// <param name="code">The error's code.</param>
    /// <param name="messageSpecific">The 4 bytes after the checksum, such as the MTU of a "packet too big" error.</param>
    /// <param name="updateChecksum"><see langword="true"/> to compute the checksums; <see langword="false"/> to leave them zero.</param>
    /// <returns>The error packet; dispose it to return its memory.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ipPacket"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="ipPacket"/> is disposed.</exception>
    public static IpPacket BuildIcmpV6Error(IpPacket ipPacket, IcmpV6Type icmpV6Type, IcmpV6Code code,
        uint messageSpecific,
        bool updateChecksum = true)
    {
        if (ipPacket is null) throw new ArgumentNullException(nameof(ipPacket));

        // Copy original IP header (40 bytes) + 8 bytes of payload into ICMP payload
        var toCopy = Math.Min(1280 - 40, ipPacket.Buffer.Length);
        var icmpIpPacket = BuildIcmpV6(ipPacket.DestinationAddress, ipPacket.SourceAddress,
            ipPacket.Buffer.Span[..toCopy]);

        var icmpPacket = icmpIpPacket.ExtractIcmpV6();
        icmpPacket.Type = icmpV6Type;
        icmpPacket.Code = (byte)code;
        icmpPacket.MessageSpecific = messageSpecific;

        if (updateChecksum)
            icmpIpPacket.UpdateAllChecksums();

        return icmpIpPacket;
    }

    // Writes the TCP segment into a packet built to carry it.
    private static IpPacket FillTcp(IpPacket ipPacket, int sourcePort, int destinationPort,
        ReadOnlySpan<byte> options, ReadOnlySpan<byte> payload)
    {
        var tcpPacket = ipPacket.BuildTcp(options.Length);
        tcpPacket.SourcePort = (ushort)sourcePort;
        tcpPacket.DestinationPort = (ushort)destinationPort;
        payload.CopyTo(tcpPacket.Payload.Span);
        options.CopyTo(tcpPacket.Options.Span);
        return ipPacket;
    }
}
