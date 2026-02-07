using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.DomainFiltering.SniExtractors.Tls;
using VpnHood.Core.DomainFiltering.SniServices;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.DomainFiltering;

public class DomainFilterService
{
    private static readonly TimeSpan QuicFlowTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TcpFlowTimeout = TimeSpan.FromSeconds(3);

    private readonly DomainFilterResolver _domainFilterResolver;
    private readonly TcpSniService _tcpSniService;
    private readonly QuicSniService _quicSniService;
    private readonly DomainFilterPolicy _domainFilterPolicy;
    private readonly EventId _sniEventId;
    private readonly int _tlsBufferSize;
    private readonly bool _trackObservations;
    private readonly List<DomainObservation> _observations = [];

    public DomainFilterService(DomainFilterPolicy domainFilterPolicy,
        bool forceLogSni, EventId sniEventId,
        int tlsBufferSize, 
        bool trackObservations = false)
    {
        _domainFilterPolicy = domainFilterPolicy;
        ForceLogSni = forceLogSni;
        _sniEventId = sniEventId;
        _tlsBufferSize = tlsBufferSize;
        _trackObservations = trackObservations;
        _domainFilterResolver = new DomainFilterResolver(domainFilterPolicy);
        _quicSniService = new QuicSniService(_domainFilterResolver, sniEventId: sniEventId, connectionTimeout: QuicFlowTimeout);
        _tcpSniService = new TcpSniService(_domainFilterResolver, sniEventId: sniEventId, connectionTimeout: TcpFlowTimeout);
    }

    public bool ForceLogSni { get; set; }

    public DomainFilterPolicy DomainFilterPolicy {
        get=> _domainFilterResolver.DomainFilterPolicy; 
        set=> _domainFilterResolver.DomainFilterPolicy = value;
    }

    public IReadOnlyList<DomainObservation> Observations => _observations;

    public bool IsEnabled =>
        ForceLogSni ||
        _domainFilterPolicy.Includes.Length > 0 ||
        _domainFilterPolicy.Excludes.Length > 0 ||
        _domainFilterPolicy.Blocks.Length > 0;

    public PacketFilterResult Process(IpPacket ipPacket)
    {
        var result = ipPacket.Protocol switch {
            IpProtocol.Tcp => _tcpSniService.ProcessPacket(ipPacket),
            IpProtocol.Udp => _quicSniService.ProcessPacket(ipPacket),
            _ => PacketFilterResult.Passthrough()
        };

        // Force log SNI if enabled
        if (result.IsNewFlow &&  !string.IsNullOrEmpty(result.DomainName))
            VhLogger.Instance.LogDebug(_sniEventId,
                "Domain: {Domain}, DestEp: {IP}",
                VhLogger.FormatHostName(result.DomainName), VhLogger.Format(ipPacket.GetDestinationEndPoint()));
        
        // Track observation if enabled
        if (_trackObservations && result.IsNewFlow && !string.IsNullOrEmpty(result.DomainName))
            _observations.Add(new DomainObservation(result.DomainName, result.Action, FastDateTime.Now));
        
        return result;
    }


    public async Task<DomainStreamFilterResult> Process(Stream tlsStream, IPAddress remoteAddress,
        CancellationToken cancellationToken)
    {
        // none if domain filter is empty
        if (!IsEnabled)
            return new DomainStreamFilterResult {
                Action = DomainFilterAction.None,
                DomainName = null,
                ReadData = Memory<byte>.Empty
            };

        // extract SNI
        var sniData = await TlsSniExtractor.ExtractSni(tlsStream, _sniEventId, _tlsBufferSize, cancellationToken).Vhc();
        if (!string.IsNullOrEmpty(sniData.DomainName)) {
            VhLogger.Instance.LogInformation(_sniEventId,
                "Domain: {Domain}, DestEp: {IP}",
                VhLogger.FormatHostName(sniData.DomainName), VhLogger.Format(remoteAddress));
        }

        // no SNI
        var resolver = new DomainFilterResolver(_domainFilterPolicy);
        var res = new DomainStreamFilterResult {
            DomainName = sniData.DomainName,
            ReadData = sniData.ReadData,
            Action = resolver.Process(sniData.DomainName)
        };

        // Track observation if enabled
        if (_trackObservations && !string.IsNullOrEmpty(res.DomainName))
            _observations.Add(new DomainObservation(res.DomainName, res.Action, FastDateTime.Now));

        return res;
    }
}