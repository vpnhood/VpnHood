namespace VpnHood.Core.Filtering.DomainFiltering.SniExtractors;

/// <summary>
/// Result of SNI extraction from a packet.
/// </summary>
internal readonly struct PacketSniResult
{
    /// <summary>
    /// The extracted domain name, null if not found.
    /// </summary>
    public string? DomainName { get; init; }

    /// <summary>
    /// True if more packets are needed to complete SNI extraction.
    /// </summary>
    public bool NeedMore { get; init; }

    /// <summary>
    /// Protocol-specific state object for multi-packet extraction.
    /// </summary>
    public object? State { get; init; }

    /// <summary>
    /// True if a ClientHello began and was not complete in time, so its name is not known and its flow fails closed.
    /// </summary>
    public bool IsIncomplete { get; init; }

    /// <summary>
    /// The ID of the connection the result is for, where the protocol names one and it is proved, or null.
    /// </summary>
    public byte[]? ConnectionId { get; init; }

    public static PacketSniResult NotFound => new() { DomainName = null, NeedMore = false, State = null };
    public static PacketSniResult Found(string domainName) => new() { DomainName = domainName, NeedMore = false, State = null };
    public static PacketSniResult Pending(object state) => new() { DomainName = null, NeedMore = true, State = state };
}
