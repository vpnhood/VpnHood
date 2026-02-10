using Microsoft.Extensions.Logging;
using VpnHood.Core.DomainFiltering.SniExtractors;
using VpnHood.Core.DomainFiltering.SniExtractors.Tcp;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.DomainFiltering.SniFilteringServices;

/// <summary>
/// SNI extraction service for TCP (TLS on port 443) traffic.
/// Note: TCP extraction by packet is useless service, because TCP SNI is come after TCP handshake, and it will be too late to exclude connection
/// evan if we establish our own handshake, we can not simulate the rest
/// Use TcpStreamSniFilteringService instead as proxy
/// </summary>
[Obsolete("Use StreamSniExtractor")]
public class TcpSniFilteringService(
    DomainFilterResolver domainFilterResolver,
    TimeSpan flowTimeout,
    EventId? sniEventId)
    : PacketSniFilteringService(domainFilterResolver, flowTimeout, sniEventId)
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

    protected override bool IsFlowEnd(IpPacket ipPacket)
    {
        if (ipPacket.Protocol != IpProtocol.Tcp)
            return false;

        var tcpPacket = ipPacket.ExtractTcp();
        return tcpPacket.Finish || tcpPacket.Reset;
    }

    protected override PacketSniResult ExtractSni(
        ReadOnlySpan<byte> payload,
        object? state,
        long nowTicks)
    {
        return TcpSniExtractor.TryExtractSniFromTcpPayload(payload, state, nowTicks);
    }
}
