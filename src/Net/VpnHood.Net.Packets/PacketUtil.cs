// ReSharper disable UnusedMember.Global

using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.Packets;

/// <summary>
/// Provides helpers to read an IP packet's length and to compute Internet checksums.
/// </summary>
public static class PacketUtil
{
    /// <summary>
    /// Reads an IP packet's total length from its header, without parsing the rest.
    /// </summary>
    /// <param name="buffer">The packet's first bytes: at least 4 for IPv4 and 6 for IPv6.</param>
    /// <returns>The packet's length in bytes, header included.</returns>
    /// <exception cref="ArgumentException"><paramref name="buffer"/> is too short to hold the length.</exception>
    /// <exception cref="IndexOutOfRangeException"><paramref name="buffer"/> is empty.</exception>
    /// <exception cref="Exception">The packet is neither IPv4 nor IPv6.</exception>
    public static int ReadPacketLength(ReadOnlySpan<byte> buffer)
    {
        var ipVersion = IpPacket.GetPacketVersion(buffer);
        return ipVersion switch {
            // v4
            IpVersion.IPv4 => IpV4Packet.GetPacketLength(buffer),
            IpVersion.IPv6 => IpV6Packet.GetPacketLength(buffer),
            _ => throw new Exception("Unknown packet version.")
        };
    }

    /// <summary>
    /// Computes the checksum of a TCP, UDP or ICMPv6 packet: the Internet checksum of its IP pseudo-header and its
    /// bytes.
    /// </summary>
    /// <param name="sourceAddress">The source address of the IP packet: 4 bytes for IPv4, 16 for IPv6.</param>
    /// <param name="destinationAddress">The destination address of the IP packet, of the same length.</param>
    /// <param name="protocol">The protocol number the pseudo-header carries, such as 6 for TCP.</param>
    /// <param name="data">The upper-layer packet, header and payload, with its checksum field zero.</param>
    /// <returns>The checksum, as a number to write in network byte order.</returns>
    /// <exception cref="ArgumentException">The addresses are not both 4 or both 16 bytes long.</exception>
    public static ushort ComputeChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress,
        byte protocol, ReadOnlySpan<byte> data)
    {
        if (sourceAddress.Length == 4 && destinationAddress.Length == 4) {
            // IPv4 pseudo header is 12 bytes
            Span<byte> pseudoHeader = stackalloc byte[12];
            sourceAddress.CopyTo(pseudoHeader[..4]);
            destinationAddress.CopyTo(pseudoHeader[4..8]);
            pseudoHeader[8] = 0;
            pseudoHeader[9] = protocol;
            pseudoHeader[10] = (byte)(data.Length >> 8);
            pseudoHeader[11] = (byte)data.Length;
            return OnesComplementSum(pseudoHeader, data);
        }

        if (sourceAddress.Length == 16 && destinationAddress.Length == 16) {
            // IPv6 pseudo header is 40 bytes
            Span<byte> pseudoHeader = stackalloc byte[40];
            sourceAddress.CopyTo(pseudoHeader[..16]);
            destinationAddress.CopyTo(pseudoHeader[16..32]);
            pseudoHeader[32] = (byte)(data.Length >> 8);
            pseudoHeader[33] = (byte)data.Length;
            pseudoHeader[34] = 0;
            pseudoHeader[35] = 0;
            pseudoHeader[36] = 0;
            pseudoHeader[37] = 0;
            pseudoHeader[38] = 0;
            pseudoHeader[39] = protocol; // the upper layer's protocol
            return OnesComplementSum(pseudoHeader, data);
        }

        throw new ArgumentException("Invalid address lengths for checksum calculation.");
    }

    /// <summary>
    /// Computes the Internet checksum (RFC 1071) of some bytes. The same as
    /// <see cref="ChecksumUtils.OnesComplementSum(ReadOnlySpan{byte})"/>.
    /// </summary>
    /// <param name="data">The bytes to sum, with any checksum field among them zero.</param>
    /// <returns>The checksum, as a number to write in network byte order.</returns>
    public static ushort OnesComplementSum(ReadOnlySpan<byte> data)
    {
        return OnesComplementSum(ReadOnlySpan<byte>.Empty, data);
    }

    /// <summary>
    /// Computes the Internet checksum (RFC 1071) of a pseudo-header followed by some bytes. The same as
    /// <see cref="ChecksumUtils.OnesComplementSum(ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>.
    /// </summary>
    /// <param name="pseudoHeader">The bytes summed first, such as an IP pseudo-header; may be empty.</param>
    /// <param name="data">The bytes summed after them, with any checksum field among them zero.</param>
    /// <returns>The checksum, as a number to write in network byte order.</returns>
    public static ushort OnesComplementSum(ReadOnlySpan<byte> pseudoHeader, ReadOnlySpan<byte> data)
    {
        return ChecksumUtils.OnesComplementSum(pseudoHeader, data);
    }
}
