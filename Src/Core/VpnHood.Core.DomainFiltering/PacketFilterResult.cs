using VpnHood.Core.Packets;

namespace VpnHood.Core.DomainFiltering;

/// <summary>
/// Result of packet domain filtering.
/// Used for both QUIC (UDP) and TCP-based protocols.
/// </summary>
public readonly struct PacketFilterResult
{
    /// <summary>
    /// The filter action to take for these packets.
    /// </summary>
    public DomainFilterAction Action { get; }

    /// <summary>
    /// The extracted domain name (if found).
    /// </summary>
    public string? DomainName { get; }

    /// <summary>
    /// Packets to route (may include buffered packets from previous calls).
    /// All packets in this list should be routed together based on the Action.
    /// </summary>
    public IReadOnlyList<IpPacket> Packets { get; }

    /// <summary>
    /// True if packets are being buffered (waiting for more data to extract SNI).
    /// When true, the caller should not route the packet yet.
    /// </summary>
    public bool IsBuffered { get; }

    /// <summary>
    /// True if this packet doesn't match the filter criteria (e.g., wrong port, wrong protocol).
    /// When true, the caller should use default routing.
    /// </summary>
    public bool IsPassthrough { get; }

    public PacketFilterResult(DomainFilterAction action, string? domainName, IReadOnlyList<IpPacket> packets)
    {
        Action = action;
        DomainName = domainName;
        Packets = packets;
        IsBuffered = false;
        IsPassthrough = false;
    }

    private PacketFilterResult(bool isBuffered, bool isPassthrough, IpPacket? packet)
    {
        Action = DomainFilterAction.None;
        DomainName = null;
        Packets = packet != null ? [packet] : [];
        IsBuffered = isBuffered;
        IsPassthrough = isPassthrough;
    }

    /// <summary>
    /// Creates a result indicating packets are being buffered.
    /// </summary>
    public static PacketFilterResult Buffered() => new(isBuffered: true, isPassthrough: false, packet: null);

    /// <summary>
    /// Creates a result indicating the packet should use default routing (passthrough).
    /// </summary>
    public static PacketFilterResult Passthrough(IpPacket packet) => new(isBuffered: false, isPassthrough: true, packet: packet);
}
