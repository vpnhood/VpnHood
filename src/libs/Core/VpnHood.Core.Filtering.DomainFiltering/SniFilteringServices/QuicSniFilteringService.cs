using Microsoft.Extensions.Logging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering.SniExtractors;
using VpnHood.Core.Filtering.DomainFiltering.SniExtractors.Quic;
using VpnHood.Net.Packets;
using VpnHood.Net.Packets.Extensions;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Filtering.DomainFiltering.SniFilteringServices;

/// <summary>
/// SNI extraction service for QUIC (UDP port 443) traffic.
/// </summary>
internal class QuicSniFilteringService(
    IDomainFilter domainFilter,
    TimeSpan flowTimeout,
    EventId? sniEventId)
    : PacketSniFilteringService(domainFilter, flowTimeout, sniEventId)
{
    protected override string ProtocolName => "QUIC";


    protected override bool TryValidateAndExtractPayload(
        IpPacket ipPacket,
        out FlowKey flowKey,
        out ReadOnlySpan<byte> payload)
    {
        flowKey = default;
        payload = default;

        // Only process UDP packets
        if (ipPacket.Protocol != IpProtocol.Udp)
            return false;

        var udpPacket = ipPacket.ExtractUdp();
        flowKey = new FlowKey(
            new IpEndPointValue(ipPacket.SourceAddressValue, udpPacket.SourcePort),
            new IpEndPointValue(ipPacket.DestinationAddressValue, udpPacket.DestinationPort));
        payload = udpPacket.Payload.Span;
        return true;
    }

    // A client that keeps its socket may open another connection to the same server, as on a CDN's address that serves
    // many names, while the one before it lives or after it. A connection is known by the destination connection ID
    // of its first Initials, which decrypt with the keys of that ID
    protected override FlowPacketKind GetPacketKind(ReadOnlySpan<byte> payload, FlowInfo flowInfo, long nowTicks)
    {
        // a short-header packet, as nearly all of a flow's are: a byte's test
        if (payload.Length == 0 || (payload[0] & 0x80) == 0)
            return FlowPacketKind.Other;

        // Of the connection being read: its Initials, and its 0-RTT packets, which wait for its verdict with them.
        // Where the read's time is out and the connection sends its ClientHello again, the read begins again with
        // that datagram, and can be whole: the read that is over would end at it, and leave each datagram that
        // follows to begin a read of its own, one with no start, at every resend
        if (flowInfo.SniState is QuicSniState readState && QuicSniExtractor.IsOfConnection(payload, readState.Dcid))
            return QuicSniExtractor.IsOver(readState, nowTicks) && QuicSniExtractor.IsAuthenticInitial(payload, nowTicks)
                ? FlowPacketKind.ConnectionStart
                : FlowPacketKind.ReadPacket;

        // of the connection read last: its Initials sent again, and its 0-RTT packets, which go as its Initials went
        if (flowInfo.ConnectionId != null && QuicSniExtractor.IsOfConnection(payload, flowInfo.ConnectionId))
            return FlowPacketKind.KnownConnection;

        // An Initial under another ID begins a connection where it decrypts with that ID's keys. One a client sends
        // after the server's first answer names the server's ID, whose keys are not its own, and begins none; each
        // such costs a decryption, a few to a connection
        return QuicSniExtractor.IsAuthenticInitial(payload, nowTicks)
            ? FlowPacketKind.ConnectionStart
            : FlowPacketKind.Other;
    }

    protected override PacketSniResult ExtractSni(
        ReadOnlySpan<byte> payload,
        object? state,
        long nowTicks)
    {
        var quicState = state as QuicSniState;
        var result = QuicSniExtractor.TryExtractSniFromUdpPayload(payload, quicState, nowTicks);

        if (result is { NeedMore: true, State: not null })
            return PacketSniResult.Pending(result.State);

        return new PacketSniResult {
            DomainName = result.DomainName,
            NeedMore = false,
            State = null,
            IsIncomplete = result.IsIncomplete,
            ConnectionId = result.ConnectionId
        };
    }
}
