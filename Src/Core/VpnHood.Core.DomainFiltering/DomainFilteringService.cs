using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.DomainFiltering.Observation;
using VpnHood.Core.DomainFiltering.SniExtractors.TlsStream;
using VpnHood.Core.DomainFiltering.SniFilteringServices;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.DomainFiltering;

public class DomainFilteringService
{
    private static readonly TimeSpan QuicFlowTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TcpFlowTimeout = TimeSpan.FromSeconds(3);

    private readonly DomainFilterResolver _filterResolver;
    private readonly TcpSniFilteringService _tcpSniService;
    private readonly QuicSniFilteringService _quicSniService;
    private readonly DomainFilteringPolicy _filteringPolicy;
    private readonly EventId _sniEventId;
    private readonly int _tlsBufferSize;
    private readonly bool _trackObservations;
    private readonly DomainObserver _observationTracker = new();

    public DomainFilteringService(DomainFilteringPolicy filteringPolicy,
        bool forceLogSni, EventId sniEventId,
        int tlsBufferSize, 
        bool trackObservations = false)
    {
        _filteringPolicy = filteringPolicy;
        ForceLogSni = forceLogSni;
        _sniEventId = sniEventId;
        _tlsBufferSize = tlsBufferSize;
        _trackObservations = trackObservations;
        _filterResolver = new DomainFilterResolver(filteringPolicy);
        _quicSniService = new QuicSniFilteringService(_filterResolver, sniEventId: sniEventId, connectionTimeout: QuicFlowTimeout);
        _tcpSniService = new TcpSniFilteringService(_filterResolver, sniEventId: sniEventId, connectionTimeout: TcpFlowTimeout);
    }

    public bool ForceLogSni { get; set; }

    public DomainFilteringPolicy FilteringPolicy {
        get=> _filterResolver.FilterPolicy; 
        set=> _filterResolver.FilterPolicy = value;
    }

    public IReadOnlyList<DomainObservation> Observations => _observationTracker.Observations;

    public bool IsEnabled =>
        ForceLogSni ||
        _filteringPolicy.Includes.Length > 0 ||
        _filteringPolicy.Excludes.Length > 0 ||
        _filteringPolicy.Blocks.Length > 0;

    public PacketSniFilterResult Process(IpPacket ipPacket)
    {
        var result = ipPacket.Protocol switch {
            IpProtocol.Tcp => _tcpSniService.ProcessPacket(ipPacket),
            IpProtocol.Udp => _quicSniService.ProcessPacket(ipPacket),
            _ => PacketSniFilterResult.Passthrough()
        };

        // Force log SNI if enabled
        if (result.IsNewFlow &&  !string.IsNullOrEmpty(result.DomainName))
            VhLogger.Instance.LogDebug(_sniEventId,
                "Domain: {Domain}, DestEp: {IP}",
                VhLogger.FormatHostName(result.DomainName), VhLogger.Format(ipPacket.GetDestinationEndPoint()));
        
        // Track observation if enabled
        if (_trackObservations && result.IsNewFlow && !string.IsNullOrEmpty(result.DomainName)) {
            var protocol = ipPacket.Protocol == IpProtocol.Tcp 
                ? DomainObservationProtocol.Tcp 
                : DomainObservationProtocol.Quic;
            _observationTracker.Track(result.DomainName, result.Action, protocol, FastDateTime.Now);
        }
        
        return result;
    }


    public async Task<StreamSniFilterResult> Process(Stream tlsStream, IPAddress remoteAddress,
        CancellationToken cancellationToken)
    {
        // none if domain filter is empty
        if (!IsEnabled)
            return new StreamSniFilterResult {
                Action = DomainFilterAction.None,
                DomainName = null,
                ReadData = Memory<byte>.Empty
            };

        // extract SNI
        var sniData = await StreamSniExtractor.ExtractSni(tlsStream, _sniEventId, _tlsBufferSize, cancellationToken).Vhc();
        if (!string.IsNullOrEmpty(sniData.DomainName)) {
            VhLogger.Instance.LogInformation(_sniEventId,
                "Domain: {Domain}, DestEp: {IP}",
                VhLogger.FormatHostName(sniData.DomainName), VhLogger.Format(remoteAddress));
        }

        // no SNI
        var resolver = new DomainFilterResolver(_filteringPolicy);
        var res = new StreamSniFilterResult {
            DomainName = sniData.DomainName,
            ReadData = sniData.ReadData,
            Action = resolver.Process(sniData.DomainName)
        };

        // Track observation if enabled
        if (_trackObservations && !string.IsNullOrEmpty(res.DomainName))
            _observationTracker.Track(res.DomainName, res.Action, DomainObservationProtocol.Tcp, FastDateTime.Now);

        return res;
    }
}