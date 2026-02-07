using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.SniFiltering.SniExtractors.Tls;
using VpnHood.Core.SniFiltering.SniServices;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.SniFiltering;
using VpnHood.Core.SniFiltering.Observation;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.SniFiltering;

public class DomainFilterService
{
    private static readonly TimeSpan QuicFlowTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TcpFlowTimeout = TimeSpan.FromSeconds(3);

    private readonly DomainFilterResolver _domainFilterResolver;
    private readonly TcpSniFilteringService _tcpSniService;
    private readonly QuicSniFilteringService _quicSniService;
    private readonly DomainFilterPolicy _sniFilterPolicy;
    private readonly EventId _sniEventId;
    private readonly int _tlsBufferSize;
    private readonly bool _trackObservations;
    private readonly DomainObserver _observationTracker = new();

    public DomainFilterService(DomainFilterPolicy sniFilterPolicy,
        bool forceLogSni, EventId sniEventId,
        int tlsBufferSize, 
        bool trackObservations = false)
    {
        _sniFilterPolicy = sniFilterPolicy;
        ForceLogSni = forceLogSni;
        _sniEventId = sniEventId;
        _tlsBufferSize = tlsBufferSize;
        _trackObservations = trackObservations;
        _domainFilterResolver = new DomainFilterResolver(sniFilterPolicy);
        _quicSniService = new QuicSniFilteringService(_domainFilterResolver, sniEventId: sniEventId, connectionTimeout: QuicFlowTimeout);
        _tcpSniService = new TcpSniFilteringService(_domainFilterResolver, sniEventId: sniEventId, connectionTimeout: TcpFlowTimeout);
    }

    public bool ForceLogSni { get; set; }

    public DomainFilterPolicy DomainFilterPolicy {
        get=> _domainFilterResolver.DomainFilterPolicy; 
        set=> _domainFilterResolver.DomainFilterPolicy = value;
    }

    public IReadOnlyList<DomainObservation> Observations => _observationTracker.Observations;

    public bool IsEnabled =>
        ForceLogSni ||
        _sniFilterPolicy.Includes.Length > 0 ||
        _sniFilterPolicy.Excludes.Length > 0 ||
        _sniFilterPolicy.Blocks.Length > 0;

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
                ? domainObservationProtocol.Tcp 
                : domainObservationProtocol.Quic;
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
        var resolver = new DomainFilterResolver(_sniFilterPolicy);
        var res = new StreamSniFilterResult {
            DomainName = sniData.DomainName,
            ReadData = sniData.ReadData,
            Action = resolver.Process(sniData.DomainName)
        };

        // Track observation if enabled
        if (_trackObservations && !string.IsNullOrEmpty(res.DomainName))
            _observationTracker.Track(res.DomainName, res.Action, domainObservationProtocol.Tcp, FastDateTime.Now);

        return res;
    }
}