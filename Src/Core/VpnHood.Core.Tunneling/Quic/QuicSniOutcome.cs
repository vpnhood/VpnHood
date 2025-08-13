namespace VpnHood.Core.Tunneling.Quic;

public enum QuicSniOutcome
{
    Found,
    NeedMore,
    GiveUp,
    NotInitial
}