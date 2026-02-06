using VpnHood.Core.DomainFiltering.SniExtractors.Quic;

namespace VpnHood.Core.DomainFiltering.SniExtractors;

internal struct QuicSniResultNew : IPacketSniResult
{
    public required string? DomainName { get; init; }
    public required bool NeedMore { get; init; }
    public QuicSniState? State { get; init; }

}
