using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.DomainFiltering;

/// <summary>
/// Composite packet domain filter that handles both QUIC (UDP) and TCP protocols.
/// Delegates to protocol-specific filters and provides unified result handling.
/// </summary>
public class PacketDomainFilter(DomainFilter domainFilter, bool forceLogSni, EventId eventId)
    : IDisposable
{
    private readonly QuicDomainFilter _quicFilter = new(domainFilter, forceLogSni);
    private bool _disposed;

    /// <summary>
    /// Whether domain filtering is enabled for any protocol.
    /// </summary>
    public bool IsEnabled {
        get => field ||
               domainFilter.Includes.Length > 0 ||
               domainFilter.Excludes.Length > 0 ||
               domainFilter.Blocks.Length > 0;
    } = forceLogSni;

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

        // Try QUIC filter first (UDP:443)
        if (ipPacket.Protocol == IpProtocol.Udp) {
            var result = _quicFilter.Process(ipPacket);

            // Log SNI if found
            if (result is { IsPassthrough: false, IsBuffered: false, DomainName: not null }) {
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

        return DomainFilterService.ProcessInternal(domain, domainFilter);
    }

    private void LogSni(string domain, System.Net.IPAddress destinationAddress, string protocol)
    {
        VhLogger.Instance.LogInformation(eventId,
            "{Protocol} Domain: {Domain}, DestIp: {IP}",
            protocol, VhLogger.FormatHostName(domain), VhLogger.Format(destinationAddress));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _quicFilter.Dispose();
    }
}
