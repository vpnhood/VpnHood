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
    // many names: an Initial that decrypts with the keys of its own connection ID, under another ID than the one the
    // verdict is for. A long-header packet alone is looked at, so a flow's stream of short ones costs a byte's test,
    // and an Initial's ID that did not decrypt is kept, so the client's later Initials, under the server's ID, cost a
    // decryption once
    protected override bool IsNewFlow(ReadOnlySpan<byte> payload, FlowInfo flowInfo, long nowTicks)
    {
        if (payload.Length == 0 || (payload[0] & 0x80) == 0 ||
            !QuicSniExtractor.TryGetInitialConnectionId(payload, out var connectionId) ||
            IsSameId(connectionId, flowInfo.ConnectionId) || IsSameId(connectionId, flowInfo.RejectedConnectionId))
            return false;

        if (QuicSniExtractor.IsAuthenticInitial(payload, nowTicks))
            return true;

        flowInfo.RejectedConnectionId = connectionId.ToArray();
        return false;
    }

    private static bool IsSameId(ReadOnlySpan<byte> connectionId, byte[]? other) =>
        other != null && connectionId.SequenceEqual(other);

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
