using System.Collections.Concurrent;
using System.Net;
using VpnHood.Core.DomainFiltering.Quic;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.DomainFiltering;

/// <summary>
/// Handles QUIC SNI extraction for domain filtering.
/// Since SNI may span multiple packets, this class buffers packets until SNI is determined.
/// All buffered packets are then routed together based on the filter decision.
/// </summary>
public class QuicDomainFilter(DomainFilter domainFilter, bool forceLogSni) : IPacketDomainFilter
{
    private readonly ConcurrentDictionary<FlowKey, QuicFlowState> _flows = new();
    private readonly TimeSpan _flowTimeout = TimeSpan.FromMilliseconds(500);
    private DateTime _lastCleanupTime = DateTime.MinValue;
    private bool _disposed;

    public bool IsEnabled =>
        forceLogSni ||
        domainFilter.Includes.Length > 0 ||
        domainFilter.Excludes.Length > 0 ||
        domainFilter.Blocks.Length > 0;

    /// <summary>
    /// Process a UDP packet that might be QUIC.
    /// Returns the routing decision and any buffered packets that should be sent together.
    /// </summary>
    public PacketFilterResult Process(IpPacket ipPacket)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(QuicDomainFilter));

        // Only process UDP packets on port 443
        if (ipPacket.Protocol != IpProtocol.Udp)
            return PacketFilterResult.Passthrough(ipPacket);

        var udpPacket = ipPacket.ExtractUdp();
        if (udpPacket.DestinationPort != 443)
            return PacketFilterResult.Passthrough(ipPacket);

        // If domain filtering is disabled, pass through
        if (!IsEnabled)
            return PacketFilterResult.Passthrough(ipPacket);

        var flowKey = new FlowKey(
            ipPacket.SourceAddress,
            ipPacket.DestinationAddress,
            udpPacket.SourcePort,
            udpPacket.DestinationPort);

        // Check if we already have a decision for this flow
        if (_flows.TryGetValue(flowKey, out var existingState) && existingState.Decision != null) {
            existingState.LastUsedTime = FastDateTime.Now;
            return new PacketFilterResult(
                existingState.Decision.Value,
                existingState.DomainName,
                [ipPacket]);
        }

        // Try to extract SNI from this packet
        var udpPayload = udpPacket.Payload.Span;
        var sniState = existingState?.SniState;
        var result = QuicSniExtractorStateful.TryExtractSniFromUdpPayload(udpPayload, sniState);

        return result.Outcome switch {
            QuicSniOutcome.NotInitial => HandleNotInitial(flowKey, ipPacket, existingState),
            QuicSniOutcome.Found => HandleSniFound(flowKey, ipPacket, existingState, result.Sni!),
            QuicSniOutcome.NeedMore => HandleNeedMore(flowKey, ipPacket, existingState, result.SniState!),
            QuicSniOutcome.GiveUp => HandleGiveUp(flowKey, ipPacket, existingState),
            _ => PacketFilterResult.Passthrough(ipPacket)
        };
    }

    private PacketFilterResult HandleNotInitial(FlowKey flowKey, IpPacket ipPacket, QuicFlowState? existingState)
    {
        // Not a QUIC Initial packet - if we have buffered packets, release them all
        if (existingState != null) {
            var packets = existingState.BufferedPackets;
            packets.Add(ipPacket);
            _flows.TryRemove(flowKey, out _);
            return new PacketFilterResult(DomainFilterAction.None, null, packets);
        }

        return PacketFilterResult.Passthrough(ipPacket);
    }

    private PacketFilterResult HandleSniFound(FlowKey flowKey, IpPacket ipPacket, QuicFlowState? existingState, string sni)
    {
        var action = DomainFilterService.ProcessInternal(sni, domainFilter);

        // Collect all buffered packets plus current one
        var packets = existingState?.BufferedPackets ?? [];
        packets.Add(ipPacket);

        // Update state with decision for future packets in this flow
        var state = existingState ?? new QuicFlowState();
        state.DomainName = sni;
        state.Decision = action;
        state.BufferedPackets = []; // Clear buffer since we're releasing them
        state.SniState = null;
        state.LastUsedTime = FastDateTime.Now;
        _flows[flowKey] = state;

        return new PacketFilterResult(action, sni, packets);
    }

    private PacketFilterResult HandleNeedMore(FlowKey flowKey, IpPacket ipPacket, QuicFlowState? existingState, QuicSniState sniState)
    {
        // Buffer this packet and wait for more
        var state = existingState ?? new QuicFlowState();
        state.SniState = sniState;
        state.BufferedPackets.Add(ipPacket);
        state.LastUsedTime = FastDateTime.Now;
        _flows[flowKey] = state;

        // Cleanup old flows periodically
        CleanupExpiredFlows();

        // Return empty result - packets are buffered
        return PacketFilterResult.Buffered();
    }

    private PacketFilterResult HandleGiveUp(FlowKey flowKey, IpPacket ipPacket, QuicFlowState? existingState)
    {
        // Could not extract SNI - release all buffered packets with None action
        var packets = existingState?.BufferedPackets ?? [];
        packets.Add(ipPacket);

        // Mark flow as decided (None = pass through)
        var state = existingState ?? new QuicFlowState();
        state.Decision = DomainFilterAction.None;
        state.BufferedPackets = [];
        state.SniState = null;
        state.LastUsedTime = FastDateTime.Now;
        _flows[flowKey] = state;

        return new PacketFilterResult(DomainFilterAction.None, null, packets);
    }

    private void CleanupExpiredFlows()
    {
        var now = FastDateTime.Now;
        if (now - _lastCleanupTime < TimeSpan.FromSeconds(1))
            return;

        _lastCleanupTime = now;

        foreach (var kvp in _flows) {
            if (now - kvp.Value.LastUsedTime > _flowTimeout) {
                if (_flows.TryRemove(kvp.Key, out var state)) {
                    // Dispose buffered packets
                    foreach (var packet in state.BufferedPackets)
                        packet.Dispose();
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Dispose all buffered packets
        foreach (var kvp in _flows) {
            foreach (var packet in kvp.Value.BufferedPackets)
                packet.Dispose();
        }

        _flows.Clear();
    }

    private readonly record struct FlowKey(
        IPAddress SourceAddress,
        IPAddress DestinationAddress,
        ushort SourcePort,
        ushort DestinationPort);

    private class QuicFlowState
    {
        public QuicSniState? SniState { get; set; }
        public string? DomainName { get; set; }
        public DomainFilterAction? Decision { get; set; }
        public List<IpPacket> BufferedPackets { get; set; } = [];
        public DateTime LastUsedTime { get; set; } = FastDateTime.Now;
    }
}
