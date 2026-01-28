namespace VpnHood.Core.DomainFiltering.Quic;

public enum QuicSniOutcome
{
    Found,
    NeedMore,
    GiveUp,
    NotInitial
}