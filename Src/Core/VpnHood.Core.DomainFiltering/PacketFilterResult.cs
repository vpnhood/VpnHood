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

    public PacketFilterResult(DomainFilterAction action, string? domainName, IReadOnlyList<IpPacket> packets)
    {
        Action = action;
        DomainName = domainName;
        Packets = packets;
    }

    private PacketFilterResult(bool needMore)
    {
        NeedMore = needMore;
    }
    internal PacketFilterResult CreateNeedMore() => new PacketFilterResult(true);
}