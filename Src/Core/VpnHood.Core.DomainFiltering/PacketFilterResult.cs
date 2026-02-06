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
    public DomainFilterAction Action { get; } = DomainFilterAction.None;

    /// <summary>
    /// The extracted domain name (if found).
    /// </summary>
    public string? DomainName { get; }

    /// <summary>
    /// Packets to route (may include buffered packets from previous calls).
    /// All packets in this list should be routed together based on the Action.
    /// </summary>
    public IReadOnlyList<IpPacket> Packets { get; } = [];

    /// <summary>
    /// True if packets are being buffered (waiting for more data to extract SNI).
    /// When true, the caller should not route the packet yet.
    /// </summary>
    public bool NeedMore { get; }

    /// <summary>
    /// True if the packet should be passed through without domain filtering.
    /// </summary>
    public bool IsPassthrough { get; }

    public PacketFilterResult(DomainFilterAction action, string? domainName, IReadOnlyList<IpPacket> packets)
    {
        Action = action;
        DomainName = domainName;
        Packets = packets;
    }

    public PacketFilterResult(bool needMore)
    {
        NeedMore = needMore;
    }

    private PacketFilterResult(IpPacket packet, bool isPassthrough)
    {
        IsPassthrough = isPassthrough;
        Packets = [packet];
    }
    
    public static PacketFilterResult CreateNeedMore() => new PacketFilterResult(true);
    
    public static PacketFilterResult Passthrough(IpPacket packet) => new PacketFilterResult(packet, true);
    
    public static PacketFilterResult Buffered() => new PacketFilterResult(true);
}