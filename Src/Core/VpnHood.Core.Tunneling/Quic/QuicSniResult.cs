namespace VpnHood.Core.Tunneling.Quic;

public readonly record struct QuicSniResult(QuicSniOutcome Outcome, string? Sni, QuicSniState? SniState);