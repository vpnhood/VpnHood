using System;
using Microsoft.Extensions.Logging;
using VpnHood.Core.DomainFiltering.SniServices;
using VpnHood.Core.Packets;

namespace VpnHood.Core.DomainFiltering;

/// <summary>
/// Composite packet domain filter that handles both QUIC (UDP) and TCP protocols.
/// Uses QuicSniService for QUIC/UDP and TcpSniService for TCP/TLS traffic.
/// </summary>
public class PacketDomainFilter : IDisposable
{
    // Tuned flow timeouts for collecting SNI across packets.
    // QUIC Initial reassembly is fast; TCP TLS handshakes may span a few RTTs.
    private static readonly TimeSpan QuicFlowTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TcpFlowTimeout = TimeSpan.FromSeconds(3);

    private readonly DomainFilter _domainFilter;
    private readonly QuicSniService _quicSniService;
    private readonly TcpSniService _tcpSniService;
    private bool _disposed;

    public PacketDomainFilter(DomainFilter domainFilter, EventId? sniEventId)
    {
        _domainFilter = domainFilter;
        var resolver = new DomainFilterResolver(domainFilter);

        _quicSniService = new QuicSniService(resolver, QuicFlowTimeout, sniEventId);
        _tcpSniService = new TcpSniService(resolver, TcpFlowTimeout, sniEventId);
    }


    /// <summary>
    /// Process any IP packet for domain filtering.
    /// Automatically routes to the appropriate protocol handler (QUIC for UDP:443, TCP:443 for TLS).
    /// </summary>
    /// <param name="ipPacket">The IP packet to process.</param>
    /// <returns>
    /// The filter result. Callers should check:
    /// - NeedMore: packet is held, don't route yet
    /// - IsPassthrough: use default routing for this packet
    /// - Otherwise: route Packets according to Action
    /// </returns>
    public PacketFilterResult Process(IpPacket ipPacket)
    {

        // Route to appropriate service based on protocol
        return ipPacket.Protocol switch {
            IpProtocol.Udp => _quicSniService.ProcessPacket(ipPacket),
            IpProtocol.Tcp => _tcpSniService.ProcessPacket(ipPacket),
            _ => PacketFilterResult.Passthrough(ipPacket)
        };
    }

    /// <summary>
    /// Process a domain name directly and return the filter action.
    /// Useful for stream-based TLS where SNI is extracted separately.
    /// </summary>
    public DomainFilterAction ProcessDomain(string? domain)
    {
        if (string.IsNullOrEmpty(domain))
            return DomainFilterAction.None;

        var resolver = new DomainFilterResolver(_domainFilter);
        return resolver.Process(domain);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _quicSniService.Dispose();
        _tcpSniService.Dispose();
        GC.SuppressFinalize(this);
    }
}
