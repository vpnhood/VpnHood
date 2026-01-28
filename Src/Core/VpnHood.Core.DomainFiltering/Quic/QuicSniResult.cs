namespace VpnHood.Core.DomainFiltering.Quic;

// ReSharper disable NotAccessedPositionalProperty.Global
public readonly record struct QuicSniResult(
    QuicSniOutcome Outcome,
    string? Sni,
    QuicSniState? SniState);