using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Net.Packets;

namespace VpnHood.Core.Filtering.DomainFiltering;

/// <summary>
/// Represents the domain filter's answer for a packet, which may hold the packet back, or release packets of its flow
/// held before.
/// </summary>
public readonly struct PacketSniFilterResult
{
    /// <summary>
    /// Gets the verdict for the packet and for <see cref="Packets"/>. With <see cref="NeedMore"/>, it is
    /// <see cref="FilterAction.Block"/>, which no packet is to get then: the service holds the packet.
    /// </summary>
    public FilterAction Action { get; }

    /// <summary>Gets the flow's domain name, or <see langword="null"/> when none was found.</summary>
    public string? DomainName { get; }

    /// <summary>
    /// Gets a value indicating whether this result is the one in which the flow's name was found and its verdict
    /// first decided.
    /// </summary>
    public bool IsNewFlow { get; }

    /// <summary>
    /// Gets a value indicating whether the service holds the packet until more of the flow arrives to find its name:
    /// route nothing then.
    /// </summary>
    public bool NeedMore { get; }

    /// <summary>
    /// Gets the flow's packets the service held before this one, released now: route them, under
    /// <see cref="Action"/>, before the packet. They are the caller's to dispose. Empty when no held packet is
    /// released, and <see langword="null"/> in a <see langword="default"/> instance.
    /// </summary>
    public IReadOnlyList<IpPacket> Packets { get; init; } = [];

    internal PacketSniFilterResult(FilterAction action, string? domainName,
        IReadOnlyList<IpPacket>? packets, bool isNewFlow)
    {
        Action = action;
        DomainName = domainName;
        IsNewFlow = isNewFlow;
        Packets = packets ?? Packets;

    }

    internal PacketSniFilterResult(FilterAction action)
    {
        Action = action;
    }

    internal PacketSniFilterResult(bool needMore)
    {
        Action = FilterAction.Block;
        NeedMore = needMore;
    }

    internal static PacketSniFilterResult Pending() => new(true);
    internal static PacketSniFilterResult Passthrough() => new(FilterAction.Default);

}