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
    private readonly DomainFilter _domainFilter;
    private readonly QuicSniService _quicSniService;
    private readonly TcpSniService _tcpSniService;
    private bool _disposed;

    public PacketDomainFilter(DomainFilter domainFilter, EventId? sniEventId, TimeSpan? connectionTimeout = null)
    {
        _domainFilter = domainFilter;
        var resolver = new DomainFilterResolver(domainFilter);
        var timeout = connectionTimeout ?? TimeSpan.FromMilliseconds(500);
        
        _quicSniService = new QuicSniService(resolver, timeout, sniEventId);
        _tcpSniService = new TcpSniService(resolver, timeout, sniEventId);
    }

    /// <summary>
    /// Whether domain filtering is enabled for any protocol.
    /// </summary>
    public bool IsEnabled =>
        _domainFilter.Includes.Length > 0 ||
        _domainFilter.Excludes.Length > 0 ||
        _domainFilter.Blocks.Length > 0;

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
        ObjectDisposedException.ThrowIf(_disposed, nameof(PacketDomainFilter));

        if (!IsEnabled)
            return PacketFilterResult.Passthrough(ipPacket);

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
