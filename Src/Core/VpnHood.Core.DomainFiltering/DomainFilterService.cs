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
    private readonly DomainFilter _domainFilter;
    private readonly EventId _sniEventId;
    private readonly int _tlsBufferSize;

    public DomainFilterService(DomainFilter domainFilter,
        bool forceLogSni, EventId sniEventId,
        int tlsBufferSize)
    {
        _domainFilter = domainFilter;
        ForceLogSni = forceLogSni;
        _sniEventId = sniEventId;
        _tlsBufferSize = tlsBufferSize;
        _domainFilterResolver = new DomainFilterResolver(domainFilter);
        _quicSniService = new QuicSniService(_domainFilterResolver, sniEventId: sniEventId, connectionTimeout: QuicFlowTimeout);
        _tcpSniService = new TcpSniService(_domainFilterResolver, sniEventId: sniEventId, connectionTimeout: TcpFlowTimeout);
    }

    public bool ForceLogSni { get; set; }
    
    public bool IsEnabled =>
        ForceLogSni ||
        _domainFilter.Includes.Length > 0 ||
        _domainFilter.Excludes.Length > 0 ||
        _domainFilter.Blocks.Length > 0;

    public PacketFilterResult Process(IpPacket ipPacket)
    {
        var result = ipPacket.Protocol switch {
            IpProtocol.Tcp => _tcpSniService.ProcessPacket(ipPacket),
            IpProtocol.Udp => _quicSniService.ProcessPacket(ipPacket),
            _ => PacketFilterResult.Passthrough(_domainFilterResolver.DefaultAction)
        };

        // Force log SNI if enabled
        if (result.IsNewFlow &&  !string.IsNullOrEmpty(result.DomainName))
            VhLogger.Instance.LogDebug(_sniEventId,
                "Domain: {Domain}, DestEp: {IP}",
                VhLogger.FormatHostName(result.DomainName), VhLogger.Format(ipPacket.GetDestinationEndPoint()));
        
        return result;
    }


    public async Task<DomainStreamFilterResult> Process(Stream tlsStream, IPAddress remoteAddress,
        CancellationToken cancellationToken)
    {
        // none if domain filter is empty
        if (!IsEnabled)
            return new DomainStreamFilterResult {
                Action = DomainFilterAction.Include,
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
        var resolver = new DomainFilterResolver(_domainFilter);
        var res = new DomainStreamFilterResult {
            DomainName = sniData.DomainName,
            ReadData = sniData.ReadData,
            Action = resolver.Process(sniData.DomainName)
        };

        return res;
    }
}