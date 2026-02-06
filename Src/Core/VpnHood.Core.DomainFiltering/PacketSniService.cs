using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VpnHood.Core.DomainFiltering.SniExtractors.Quic;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.DomainFiltering;

/// <summary>
/// Handles QUIC SNI extraction and domain filtering.
/// Manages packet flow state, buffering packets until SNI is determined.
/// Uses IpEndPointValue to prevent heap allocation for each packet.
/// </summary>
public class PacketSniService : IDisposable
{
    private readonly DomainFilterResolver _domainFilterResolver;
    private readonly EventId? _sniEventId;
    private readonly TimeSpan _connectionTimeout;
    private readonly ConcurrentDictionary<IpEndPointValue, FlowInfo> _flowCache = new();
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public PacketSniService(
        DomainFilterResolver domainFilterResolver, 
        TimeSpan connectionTimeout,
        EventId? sniEventId)
    {
        _domainFilterResolver = domainFilterResolver;
        _sniEventId = sniEventId;
        _connectionTimeout = connectionTimeout;
        _cleanupTimer = new Timer(CleanupExpiredFlows, this, connectionTimeout, connectionTimeout);
    }
    
    public PacketFilterResult ProcessPacket(IpPacket ipPacket)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(PacketSniService));

        // Only process UDP packets on port 443 for QUIC
        if (ipPacket.Protocol != IpProtocol.Udp)
            return PacketFilterResult.Passthrough(ipPacket);

        var udpPacket = ipPacket.ExtractUdp();
        if (udpPacket is not { DestinationPort: 443 })
            return PacketFilterResult.Passthrough(ipPacket);

        // Create flow key based on source endpoint
        var flowKey = new IpEndPointValue(ipPacket.SourceAddress, udpPacket.SourcePort);
        var nowTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;

        // Check if we already have a decision for this flow
        if (_flowCache.TryGetValue(flowKey, out var flowInfo) && flowInfo.Decision != null) {
            flowInfo.LastSeenTicks = nowTicks;
            return new PacketFilterResult(flowInfo.Decision.Value, flowInfo.DomainName, [ipPacket]);
        }

        // Extract SNI from UDP payload
        var udpPayload = udpPacket.Payload.Span;
        var sniState = flowInfo?.SniState;
        var sniResult = QuicSniExtractor.TryExtractSniFromUdpPayload(udpPayload, sniState, nowTicks);

        // SNI found
        if (sniResult.DomainName != null && _sniEventId!=null) {
            VhLogger.Instance.LogInformation(_sniEventId.Value,
                "Domain: {Domain}, DestEp: {IP}, Protocol: QUIC",
                VhLogger.FormatHostName(sniResult.DomainName), VhLogger.Format(ipPacket.DestinationAddress));
            return HandleSniFound(flowKey, ipPacket, flowInfo, sniResult.DomainName, nowTicks);
        }

        // Need more packets to extract SNI
        if (sniResult is { NeedMore: true, State: not null })
            return HandleNeedMore(flowKey, ipPacket, flowInfo, sniResult.State, nowTicks);

        // Could not extract SNI - release all buffered packets
        return HandleGiveUp(flowKey, ipPacket, flowInfo, nowTicks);
    }

    private PacketFilterResult HandleSniFound(IpEndPointValue flowKey, IpPacket ipPacket, FlowInfo? flowInfo, string domainName, long nowTicks)
    {
        var action = _domainFilterResolver.Process(domainName);

        // Collect all buffered packets plus current one
        var packets = flowInfo?.BufferedPackets ?? [];
        packets.Add(ipPacket);

        // Update state with decision for future packets in this flow
        var state = flowInfo ?? new FlowInfo();
        state.DomainName = domainName;
        state.Decision = action;
        state.BufferedPackets = []; // Clear buffer since we're releasing them
        state.SniState = null;
        state.LastSeenTicks = nowTicks;
        _flowCache[flowKey] = state;

        return new PacketFilterResult(action, domainName, packets);
    }

    private PacketFilterResult HandleNeedMore(
        IpEndPointValue flowKey, IpPacket ipPacket, FlowInfo? flowInfo, 
        QuicSniState sniState, long nowTicks)
    {
        // Buffer this packet and wait for more
        var state = flowInfo ?? new FlowInfo();
        state.SniState = sniState;
        state.BufferedPackets.Add(ipPacket);
        state.LastSeenTicks = nowTicks;
        _flowCache[flowKey] = state;

        return PacketFilterResult.Buffered();
    }

    private PacketFilterResult HandleGiveUp(IpEndPointValue flowKey, IpPacket ipPacket, FlowInfo? flowInfo, long nowTicks)
    {
        // Release all buffered packets with None action
        var packets = flowInfo?.BufferedPackets ?? [];
        packets.Add(ipPacket);

        // Mark flow as decided (None = pass through)
        var state = flowInfo ?? new FlowInfo();
        state.Decision = DomainFilterAction.None;
        state.BufferedPackets = [];
        state.SniState = null;
        state.LastSeenTicks = nowTicks;
        _flowCache[flowKey] = state;

        return new PacketFilterResult(DomainFilterAction.None, null, packets);
    }

    private static void CleanupExpiredFlows(object? state)
    {
        if (state is not PacketSniService service || service._disposed)
            return;

        var nowTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
        var timeoutTicks = service._connectionTimeout.Ticks;

        foreach (var kvp in service._flowCache) {
            if (nowTicks - kvp.Value.LastSeenTicks > timeoutTicks) {
                if (service._flowCache.TryRemove(kvp.Key, out var flowInfo)) {
                    // Dispose buffered packets
                    foreach (var packet in flowInfo.BufferedPackets)
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
        _cleanupTimer.Dispose();

        // Dispose all buffered packets
        foreach (var kvp in _flowCache) {
            foreach (var packet in kvp.Value.BufferedPackets)
                packet.Dispose();
        }

        _flowCache.Clear();
    }

    private class FlowInfo
    {
        public QuicSniState? SniState { get; set; }
        public string? DomainName { get; set; }
        public DomainFilterAction? Decision { get; set; }
        public List<IpPacket> BufferedPackets { get; set; } = [];
        public long LastSeenTicks { get; set; }
    }
}
