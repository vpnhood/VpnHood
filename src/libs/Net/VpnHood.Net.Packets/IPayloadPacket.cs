namespace VpnHood.Net.Packets;

/// <summary>
/// Represents a packet carried in the payload of an IP packet, such as a TCP segment or a UDP datagram.
/// </summary>
public interface IPayloadPacket
{
    /// <summary>
    /// Gets the packet's bytes, header and payload. When the packet is an IP packet's payload, they are that
    /// packet's own bytes, not a copy, and live only as long as its memory.
    /// </summary>
    Memory<byte> Buffer { get; }
}
