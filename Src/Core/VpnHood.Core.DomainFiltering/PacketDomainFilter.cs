using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.DomainFiltering;

/// <summary>
/// Composite packet domain filter that handles both QUIC (UDP) and TCP protocols.
/// Uses PacketSniService for QUIC/UDP and provides unified result handling.
/// </summary>
public class PacketDomainFilter : IDisposable
{
    private readonly DomainFilter _domainFilter;
    private readonly EventId _eventId;
    private readonly PacketSniService _packetSniService;
    private bool _disposed;

    public PacketDomainFilter(DomainFilter domainFilter, bool forceLogSni, EventId eventId, TimeSpan? connectionTimeout = null)
    {
        _domainFilter = domainFilter;
        _eventId = eventId;
        _packetSniService = new PacketSniService(domainFilter, connectionTimeout ?? TimeSpan.FromMilliseconds(500), forceLogSni);
    }

    /// <summary>
    /// Whether domain filtering is enabled for any protocol.
    /// </summary>
    public bool IsEnabled => _packetSniService.IsEnabled;

    /// <summary>
    /// Process any IP packet for domain filtering.
    /// Automatically routes to the appropriate protocol handler (QUIC for UDP:443, TCP for TCP).
    /// </summary>
    /// <param name="ipPacket">The IP packet to process.</param>
    /// <returns>
    /// The filter result. Callers should check:
    /// - IsBuffered: packet is held, don't route yet
    /// - IsPassthrough: use default routing for this packet
    /// - Otherwise: route Packets according to Action
    /// </returns>
    public PacketFilterResult Process(IpPacket ipPacket)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PacketDomainFilter));

        if (!IsEnabled)
            return PacketFilterResult.Passthrough(ipPacket);

        // Process QUIC (UDP:443) via PacketSniService
        if (ipPacket.Protocol == IpProtocol.Udp) {
            var result = _packetSniService.ProcessPacket(ipPacket);

            // Log SNI if found
            if (result is { IsPassthrough: false, NeedMore: false, DomainName: not null }) {
                LogSni(result.DomainName, ipPacket.DestinationAddress, "QUIC");
            }

            return result;
        }

        // TCP packets are handled via stream-based filtering (ClientHost)
        // Return passthrough so caller can use existing TCP handling
        return PacketFilterResult.Passthrough(ipPacket);
    }

    /// <summary>
    /// Process a domain name directly and return the filter action.
    /// Useful for TCP/TLS where SNI is extracted separately.
    /// </summary>
    public DomainFilterAction ProcessDomain(string? domain)
    {
        if (string.IsNullOrEmpty(domain))
            return DomainFilterAction.None;

        var resolver = new DomainFilterResolver(_domainFilter);
        return resolver.Process(domain);
    }

    private void LogSni(string domain, System.Net.IPAddress destinationAddress, string protocol)
    {
        VhLogger.Instance.LogInformation(_eventId,
            "{Protocol} Domain: {Domain}, DestIp: {IP}",
            protocol, VhLogger.FormatHostName(domain), VhLogger.Format(destinationAddress));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _packetSniService.Dispose();
    }
}
