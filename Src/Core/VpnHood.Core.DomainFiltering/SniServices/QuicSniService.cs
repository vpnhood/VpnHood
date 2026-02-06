using Microsoft.Extensions.Logging;
using VpnHood.Core.DomainFiltering.SniExtractors;
using VpnHood.Core.DomainFiltering.SniExtractors.Quic;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.DomainFiltering.SniServices;

/// <summary>
/// SNI extraction service for QUIC (UDP port 443) traffic.
/// </summary>
public class QuicSniService : PacketSniService
{
    protected override string ProtocolName => "QUIC";

    public QuicSniService(
        DomainFilterResolver domainFilterResolver,
        TimeSpan connectionTimeout,
        EventId? sniEventId)
        : base(domainFilterResolver, connectionTimeout, sniEventId)
    {
    }

    protected override bool TryValidateAndExtractPayload(
        IpPacket ipPacket,
        out IpEndPointValue flowKey,
        out ReadOnlySpan<byte> payload)
    {
        flowKey = default;
        payload = default;

        // Only process UDP packets
        if (ipPacket.Protocol != IpProtocol.Udp)
            return false;

        var udpPacket = ipPacket.ExtractUdp();
        if (udpPacket is not { DestinationPort: 443 })
            return false;

        flowKey = new IpEndPointValue(ipPacket.SourceAddress, udpPacket.SourcePort);
        payload = udpPacket.Payload.Span;
        return true;
    }

    protected override SniExtractionResult ExtractSni(
        ReadOnlySpan<byte> payload,
        object? state,
        long nowTicks)
    {
        var quicState = state as QuicSniState;
        var result = QuicSniExtractor.TryExtractSniFromUdpPayload(payload, quicState, nowTicks);

        if (result.DomainName != null)
            return SniExtractionResult.Found(result.DomainName);

        if (result is { NeedMore: true, State: not null })
            return SniExtractionResult.Pending(result.State);

        return SniExtractionResult.NotFound;
    }
}
