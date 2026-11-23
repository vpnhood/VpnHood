using Microsoft.Extensions.Logging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering.SniExtractors;
using VpnHood.Core.Filtering.DomainFiltering.SniExtractors.Tcp;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Filtering.DomainFiltering.SniFilteringServices;

/// <summary>
/// SNI extraction service for TCP (TLS on port 443) traffic. It is just useful for blocking
/// Note: TCP extraction by packet is useless service, because TCP SNI is come after TCP handshake, and it will be too late to exclude connection
/// evan if we establish our own handshake, we can not simulate the rest
/// Use TcpStreamSniFilteringService instead, with TCP forwarding
/// </summary>
internal class TcpSniFilteringService(
    IDomainFilter domainFilter,
    TimeSpan flowTimeout,
    EventId? sniEventId)
    : PacketSniFilteringService(domainFilter, flowTimeout, sniEventId)
{
    protected override string ProtocolName => "TLS";

    protected override bool TryValidateAndExtractPayload(
        IpPacket ipPacket,
        out FlowKey flowKey,
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

        // Skip packets with no data (SYN, ACK-only), but a FIN or RST, which ends the flow: passed over, it left the
        // flow's verdict to the next connection of the same addresses and ports
        if (tcpPacket.Payload.Length == 0 && !tcpPacket.Finish && !tcpPacket.Reset)
            return false;

        flowKey = new FlowKey(
            new IpEndPointValue(ipPacket.SourceAddressValue, tcpPacket.SourcePort),
            new IpEndPointValue(ipPacket.DestinationAddressValue, tcpPacket.DestinationPort));
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
