namespace VpnHood.Core.SniFiltering.SniExtractors.Quic;

internal struct QuicSniResult
{
    public required string? DomainName { get; init; }
    public required bool NeedMore { get; init; }
    public QuicSniState? State { get; init; }
}
