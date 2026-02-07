namespace VpnHood.Core.SniFiltering.SniExtractors;

/// <summary>
/// Result of SNI extraction from a packet.
/// </summary>
public readonly struct PacketSniResult
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

    public static PacketSniResult NotFound => new() { DomainName = null, NeedMore = false, State = null };
    public static PacketSniResult Found(string domainName) => new() { DomainName = domainName, NeedMore = false, State = null };
    public static PacketSniResult Pending(object state) => new() { DomainName = null, NeedMore = true, State = state };
}
