namespace VpnHood.Core.DomainFiltering.SniExtractors;

public readonly struct PacketSniResult
{
    public required string? DomainName { get; init; }
    public required bool NeedMore { get; init; }
    public required object? State { get; init; }
}
