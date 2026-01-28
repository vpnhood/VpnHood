using VpnHood.Core.Packets;

namespace VpnHood.Core.DomainFiltering;

/// <summary>
/// Interface for protocol-specific packet domain filters.
/// Implementations handle SNI extraction and domain filtering for specific protocols (QUIC, TCP/TLS, etc.).
/// </summary>
public interface IPacketDomainFilter : IDisposable
{
    /// <summary>
    /// Whether this filter is enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Process a packet and return the filtering result.
    /// </summary>
    /// <param name="ipPacket">The IP packet to process.</param>
    /// <returns>
    /// The filter result containing:
    /// - Action: what to do with the packets
    /// - Packets: list of packets to route together
    /// - IsBuffered: if true, packets are being held waiting for more data
    /// - IsPassthrough: if true, this filter doesn't handle this packet
    /// </returns>
    PacketFilterResult Process(IpPacket ipPacket);
}
