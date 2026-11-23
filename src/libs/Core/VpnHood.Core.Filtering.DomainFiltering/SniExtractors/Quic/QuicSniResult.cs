namespace VpnHood.Core.Filtering.DomainFiltering.SniExtractors.Quic;

internal struct QuicSniResult
{
    public required string? DomainName { get; init; }
    public required bool NeedMore { get; init; }
    public QuicSniState? State { get; init; }

    // a ClientHello that began and whose name could not be read, as one not complete in time or not well formed: its
    // connection fails closed
    public bool IsIncomplete { get; init; }

    // the destination connection ID of the Initials that decrypted, which the decryption proved; null for a flow none
    // of whose Initials did
    public byte[]? ConnectionId { get; init; }
}
