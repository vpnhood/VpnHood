using System;
using Microsoft.Extensions.Logging;
using VpnHood.Core.DomainFiltering.SniExtractors;
using VpnHood.Core.DomainFiltering.SniExtractors.Tcp;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.DomainFiltering.SniServices;

/// <summary>
/// SNI extraction service for TCP (TLS on port 443) traffic.
/// </summary>
public class TcpSniService(
    DomainFilterResolver domainFilterResolver,
    TimeSpan connectionTimeout,
    EventId? sniEventId)
    : PacketSniService(domainFilterResolver, connectionTimeout, sniEventId)
{
    protected override string ProtocolName => "TLS";

    protected override bool TryValidateAndExtractPayload(
        IpPacket ipPacket,
        out IpEndPointValue flowKey,
        out ReadOnlySpan<byte> payload)
    {
        flowKey = default;
        payload = default;

        // Only process TCP packets
        if (ipPacket.Protocol != IpProtocol.Tcp)
            return false;

        var tcpPacket = ipPacket.ExtractTcp();
        if (tcpPacket is not { DestinationPort: 443 })
            return false;

        // Skip non-data packets (SYN, FIN, RST, ACK-only)
        if (tcpPacket.Payload.Length == 0)
            return false;

        flowKey = new IpEndPointValue(ipPacket.SourceAddress, tcpPacket.SourcePort);
        payload = tcpPacket.Payload.Span;
        return true;
    }

    protected override SniExtractionResult ExtractSni(
        ReadOnlySpan<byte> payload,
        object? state,
        long nowTicks)
    {
        return TcpSniExtractor.TryExtractSniFromTcpPayload(payload, state, nowTicks);
    }
}
