using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering.Observation;
using VpnHood.Core.Filtering.DomainFiltering.SniExtractors.TlsStream;
using VpnHood.Core.Filtering.DomainFiltering.SniFilteringServices;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Filtering.DomainFiltering;


/// Note: TCP extraction by packet is useless service, because TCP SNI is come after TCP handshake, and it will be too late to exclude connection
/// evan if we establish our own handshake, we can not simulate the rest
/// Use TcpStreamSniFilteringService instead as proxy
public class DomainFilteringService
{
    private static readonly TimeSpan UdpFlowTimeout = TimeSpan.FromMinutes(2);

    private readonly IDomainFilter _domainFilter;
    private readonly QuicSniFilteringService _quicSniService; // Quic only, don't try Tcp SNI extraction by packet
    private readonly EventId _sniEventId;
    private readonly int _tlsBufferSize;
    private readonly bool _trackObservations;
    public DomainObserver DomainObserver { get; }
    public DomainObserverStat TcpStat { get; } = new();
    public DomainObserverStat QuicStat { get; } = new();

    public DomainFilteringService(
        IDomainFilter domainFilter,
        EventId sniEventId,
        int tlsBufferSize,
        bool trackObservations = false)
    {
        _domainFilter = domainFilter;
        _sniEventId = sniEventId;
        _tlsBufferSize = tlsBufferSize;
        _trackObservations = trackObservations;
        _quicSniService = new QuicSniFilteringService(_domainFilter, flowTimeout: UdpFlowTimeout, sniEventId: sniEventId);
        DomainObserver = new DomainObserver(sniEventId);
    }

    public bool IsEnabled { get; set; }

    public PacketSniFilterResult ProcessPacket(IpPacket ipPacket)
    {
        if (!IsEnabled)
            return PacketSniFilterResult.Passthrough();

        var result = ipPacket.Protocol switch {
            IpProtocol.Udp => _quicSniService.ProcessPacket(ipPacket),
            _ => PacketSniFilterResult.Passthrough()
        };

        // Track observation if enabled
        if (!result.IsNewFlow || string.IsNullOrEmpty(result.DomainName)) 
            return result;

        // Update QUIC stat
        QuicStat.Update(result.Action);

        // For QUIC, we can only get remote endpoint from packet, so we use destination endpoint for tracking
        if (_trackObservations) {
            DomainObserver.Track(result.DomainName,
                result.Action, DomainObservationProtocol.Quic, ipPacket.GetDestinationEndPoint());
        }

        return result;
    }


    public async Task<StreamSniFilterResult> ProcessStream(Stream tlsStream, IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken)
    {
        // none if domain filter is empty
        if (!IsEnabled)
            return StreamSniFilterResult.Passthrough();

        // extract SNI
        var sniData = await StreamSniExtractor.ExtractSni(tlsStream, _sniEventId, _tlsBufferSize, cancellationToken).Vhc();

        // no SNI
        var res = new StreamSniFilterResult {
            DomainName = sniData.DomainName,
            ReadData = sniData.ReadData,
            Action = _domainFilter.Process(sniData.DomainName)
        };

        // Track observation if enabled
        if (!string.IsNullOrEmpty(res.DomainName)) {
            // Update stream stat
            TcpStat.Update(res.Action);

            // For TCP, we can get remote endpoint from stream, so we use it for tracking
            if (_trackObservations)
                DomainObserver.Track(res.DomainName, res.Action, DomainObservationProtocol.Tcp, remoteEndPoint.ToValue());
        }

        return res;
    }
}