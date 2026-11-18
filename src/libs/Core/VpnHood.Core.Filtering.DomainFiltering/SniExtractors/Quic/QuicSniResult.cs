namespace VpnHood.Core.Filtering.DomainFiltering.SniExtractors.Quic;

internal struct QuicSniResult
{
    public required string? DomainName { get; init; }
    public required bool NeedMore { get; init; }
    public QuicSniState? State { get; init; }

    // a ClientHello that began and was not complete in time, whose flow fails closed
    public bool IsIncomplete { get; init; }

    // the destination connection ID of the Initials that decrypted, which the decryption proved; null for a flow none
    // of whose Initials did
    public byte[]? ConnectionId { get; init; }
}
