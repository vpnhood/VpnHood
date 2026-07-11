using Microsoft.Extensions.Logging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering.SniExtractors;
using VpnHood.Core.Filtering.DomainFiltering.SniExtractors.Quic;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Filtering.DomainFiltering.SniFilteringServices;

/// <summary>
/// SNI extraction service for QUIC (UDP port 443) traffic.
/// </summary>
public class QuicSniFilteringService(
    IDomainFilter domainFilter,
    TimeSpan flowTimeout,
    EventId? sniEventId)
    : PacketSniFilteringService(domainFilter, flowTimeout, sniEventId)
{
    protected override string ProtocolName => "QUIC";


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
        flowKey = new IpEndPointValue(ipPacket.SourceAddress, udpPacket.SourcePort);
        payload = udpPacket.Payload.Span;
        return true;
    }

    protected override PacketSniResult ExtractSni(
        ReadOnlySpan<byte> payload,
        object? state,
        long nowTicks)
    {
        var quicState = state as QuicSniState;
        var result = QuicSniExtractor.TryExtractSniFromUdpPayload(payload, quicState, nowTicks);

        if (result.DomainName != null)
            return PacketSniResult.Found(result.DomainName);

        if (result is { NeedMore: true, State: not null })
            return PacketSniResult.Pending(result.State);

        return PacketSniResult.NotFound;
    }
}
