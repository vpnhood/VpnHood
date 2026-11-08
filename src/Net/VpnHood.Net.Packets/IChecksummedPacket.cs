namespace VpnHood.Net.Packets;

/// <summary>
/// Represents a payload packet with an Internet checksum (RFC 1071): TCP, UDP, ICMPv4 or ICMPv6.
/// </summary>
/// <remarks>
/// The addresses are those of the IP packet that carries this one, as raw bytes: 4 for IPv4, 16 for IPv6.
/// TCP, UDP and ICMPv6 sum them in a pseudo-header; ICMPv4 ignores them.
/// </remarks>
public interface IChecksumPayloadPacket : IPayloadPacket
{
    /// <summary>
    /// Gets the checksum field as it is in the packet.
    /// </summary>
    ushort Checksum { get; }

    /// <summary>
    /// Computes the checksum and writes it to the packet's checksum field.
    /// </summary>
    /// <param name="sourceAddress">The source address of the IP packet, 4 or 16 bytes.</param>
    /// <param name="destinationAddress">The destination address of the IP packet, of the same length.</param>
    void UpdateChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress);

    /// <summary>
    /// Checks the packet's checksum field against its content.
    /// </summary>
    /// <remarks>
    /// An implementation may accept a checksum its protocol lets the sender omit, as UDP does over IPv4.
    /// </remarks>
    /// <param name="sourceAddress">The source address of the IP packet, 4 or 16 bytes.</param>
    /// <param name="destinationAddress">The destination address of the IP packet, of the same length.</param>
    /// <returns><see langword="true"/> if the checksum is valid; otherwise, <see langword="false"/>.</returns>
    bool IsChecksumValid(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress);

    /// <summary>
    /// Computes the checksum the packet's content calls for, without writing it.
    /// </summary>
    /// <remarks>
    /// The checksum field may be zeroed while the sum runs and restored after, so no other thread may use the
    /// packet meanwhile.
    /// </remarks>
    /// <param name="sourceAddress">The source address of the IP packet, 4 or 16 bytes.</param>
    /// <param name="destinationAddress">The destination address of the IP packet, of the same length.</param>
    /// <returns>The checksum, as a number to write in network byte order.</returns>
    ushort ComputeChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress);
}
