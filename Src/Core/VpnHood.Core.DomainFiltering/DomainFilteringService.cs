using Microsoft.Extensions.Logging;
using VpnHood.Core.DomainFiltering.Observation;
using VpnHood.Core.DomainFiltering.SniExtractors.TlsStream;
using VpnHood.Core.DomainFiltering.SniFilteringServices;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.DomainFiltering;


//todo add tests
/// Note: TCP extraction by packet is useless service, because TCP SNI is come after TCP handshake, and it will be too late to exclude connection
/// evan if we establish our own handshake, we can not simulate the rest
/// Use TcpStreamSniFilteringService instead as proxy
public class DomainFilteringService
{
    private static readonly TimeSpan UdpFlowTimeout = TimeSpan.FromMinutes(2);

    private readonly DomainFilterResolver _filterResolver;
    private readonly QuicSniFilteringService _quicSniService; // Quic only, don't try Tcp SNI extraction by packet
    private readonly DomainFilteringPolicy _filteringPolicy;
    private readonly EventId _sniEventId;
    private readonly int _tlsBufferSize;
    private readonly bool _trackObservations;
    public DomainObserver DomainObserver { get; }

    public DomainFilteringService(
        DomainFilteringPolicy filteringPolicy,
        bool forceLogSni, 
        EventId sniEventId,
        int tlsBufferSize,
        bool trackObservations = false)
    {
        _filteringPolicy = filteringPolicy;
        _sniEventId = sniEventId;
        _tlsBufferSize = tlsBufferSize;
        _trackObservations = trackObservations;
        _filterResolver = new DomainFilterResolver(filteringPolicy);
        _quicSniService = new QuicSniFilteringService(_filterResolver, flowTimeout: UdpFlowTimeout, sniEventId: sniEventId);
        DomainObserver = new DomainObserver(sniEventId);

        // enable service by force even without any policy to make sure SNI is logged for observation
        ForceLogSni = forceLogSni;

    }

    public bool ForceLogSni { get; set; }

    public DomainFilteringPolicy FilteringPolicy {
        get => _filterResolver.FilterPolicy;
        set => _filterResolver.FilterPolicy = value;
    }

    public IReadOnlyList<DomainObservation> Observations => DomainObserver.Observations;

    public bool IsEnabled =>
        ForceLogSni ||
        _filteringPolicy.Includes.Length > 0 ||
        _filteringPolicy.Excludes.Length > 0 ||
        _filteringPolicy.Blocks.Length > 0;

    public PacketSniFilterResult ProcessPacket(IpPacket ipPacket)
    {
        var result = ipPacket.Protocol switch {
            IpProtocol.Udp => _quicSniService.ProcessPacket(ipPacket),
            _ => PacketSniFilterResult.Passthrough()
        };

        // Track observation if enabled
        if (_trackObservations && result.IsNewFlow && !string.IsNullOrEmpty(result.DomainName)) {
            DomainObserver.Track(result.DomainName, result.Action, DomainObservationProtocol.Quic, ipPacket.GetDestinationEndPoint());
        }

        return result;
    }


    public async Task<StreamSniFilterResult> ProcessStream(Stream tlsStream, IpEndPointValue remoteEndPoint,
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

        // no SNI
        var resolver = new DomainFilterResolver(_filteringPolicy);
        var res = new StreamSniFilterResult {
            DomainName = sniData.DomainName,
            ReadData = sniData.ReadData,
            Action = resolver.Process(sniData.DomainName)
        };

        // Track observation if enabled
        if (_trackObservations && !string.IsNullOrEmpty(res.DomainName)) 
            DomainObserver.Track(res.DomainName, res.Action, DomainObservationProtocol.Tcp, remoteEndPoint);

        return res;
    }
}