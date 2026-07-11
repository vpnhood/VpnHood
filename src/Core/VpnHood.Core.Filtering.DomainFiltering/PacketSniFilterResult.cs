using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Packets;

namespace VpnHood.Core.Filtering.DomainFiltering;

/// <summary>
/// Used for both QUIC (UDP) and TCP-based protocols.
/// </summary>
public readonly struct PacketSniFilterResult
{
    /// <summary>
    /// The filter action to take for these packets.
    /// </summary>
    public FilterAction Action { get; }

    /// <summary>
    /// The extracted domain name (if found).
    /// </summary>
    public string? DomainName { get; }

    /// <summary>
    /// Is the sni filter decision for a new flow (first packet) or an existing flow.
    /// </summary>
    public bool IsNewFlow { get; }

    /// <summary>
    /// True if packets are being buffered (waiting for more data to extract SNI).
    /// When true, the caller should not route the packet yet.
    /// </summary>
    public bool NeedMore { get; }

    /// <summary>
    /// Packets to route which is just the buffered packets from previous calls.
    /// All packets in this list should be routed together based on the Action.
    /// </summary>
    public IReadOnlyList<IpPacket> Packets { get; init; } = [];

    public PacketSniFilterResult(FilterAction action, string? domainName,
        IReadOnlyList<IpPacket>? packets, bool isNewFlow)
    {
        Action = action;
        DomainName = domainName;
        IsNewFlow = isNewFlow;
        Packets = packets ?? Packets;

    }

    public PacketSniFilterResult(FilterAction action)
    {
        Action = action;
    }

    public PacketSniFilterResult(bool needMore)
    {
        Action = FilterAction.Block;
        NeedMore = needMore;
    }

    public static PacketSniFilterResult Pending() => new(true);
    public static PacketSniFilterResult Passthrough() => new(FilterAction.Default);

}