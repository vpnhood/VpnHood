namespace VpnHood.Core.DomainFiltering.SniExtractors.Quic;

public enum QuicSniOutcome
{
    // not Initial, or too much data without finding SNI
    GiveUp,

    // SNI found and valid
    Found,

    // partial SNI found, waiting for more packets to complete
    NeedMore,
    
    // packet is not a QUIC Initial, so we won't find SNI here.
    // We can release any buffered packets for this flow.
    NotInitial
}