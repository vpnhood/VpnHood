namespace VpnHood.Core.Filtering.DomainFiltering.Observation;

/// <summary>
/// Specifies the protocol of a flow in which a domain name was seen.
/// </summary>
public enum DomainObservationProtocol
{
    /// <summary>A TCP connection, by its TLS ClientHello.</summary>
    Tcp,

    /// <summary>A QUIC flow, by its first packets.</summary>
    Quic
}