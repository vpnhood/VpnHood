using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VpnHood.Core.DomainFiltering.SniExtractors;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.DomainFiltering.SniServices;

/// <summary>
/// Base class for protocol-specific SNI extraction services.
/// Handles flow management, packet buffering, and domain filtering.
/// </summary>
public abstract class PacketSniService : IDisposable
{
    private readonly ConcurrentDictionary<IpEndPointValue, FlowInfo> _flowCache = new();
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    protected DomainFilterResolver DomainFilterResolver { get; }
    protected EventId? SniEventId { get; }
    protected TimeSpan ConnectionTimeout { get; }
    protected abstract string ProtocolName { get; }

    protected PacketSniService(
        DomainFilterResolver domainFilterResolver,
        TimeSpan connectionTimeout,
        EventId? sniEventId)
    {
        DomainFilterResolver = domainFilterResolver;
        SniEventId = sniEventId;
        ConnectionTimeout = connectionTimeout;
        _cleanupTimer = new Timer(CleanupExpiredFlows, this, connectionTimeout, connectionTimeout);
    }

    /// <summary>
    /// Process an IP packet for SNI extraction and domain filtering.
    /// </summary>
    public PacketFilterResult ProcessPacket(IpPacket ipPacket)
    {
        ObjectDisposedException.ThrowIf(_disposed, GetType().Name);

        // Validate packet protocol and get payload
        if (!TryValidateAndExtractPayload(ipPacket, out var flowKey, out var payload))
            return PacketFilterResult.Passthrough();

        var nowTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;

        // Check if we already have a decision for this flow
        if (_flowCache.TryGetValue(flowKey, out var flowInfo) && flowInfo.Decision != null) {
            flowInfo.LastSeenTicks = nowTicks;
            return new PacketFilterResult(flowInfo.Decision.Value, flowInfo.DomainName, null, false);
        }

        // Extract SNI from payload
        var sniResult = ExtractSni(payload, flowInfo?.SniState, nowTicks);

        // SNI found
        if (sniResult.DomainName != null) {
            LogSni(sniResult.DomainName, ipPacket);
            return HandleSniFound(flowKey, ipPacket, flowInfo, sniResult.DomainName, nowTicks);
        }

        // Need more packets to extract SNI
        if (sniResult is { NeedMore: true, State: not null })
            return HandleNeedMore(flowKey, ipPacket, flowInfo, sniResult.State, nowTicks);

        // Could not extract SNI - release all buffered packets
        return HandleGiveUp(flowKey, flowInfo, nowTicks);
    }

    /// <summary>
    /// Validate the packet and extract the flow key and payload.
    /// </summary>
    protected abstract bool TryValidateAndExtractPayload(
        IpPacket ipPacket,
        out IpEndPointValue flowKey,
        out ReadOnlySpan<byte> payload);

    /// <summary>
    /// Extract SNI from the payload using protocol-specific logic.
    /// </summary>
    protected abstract SniExtractionResult ExtractSni(
        ReadOnlySpan<byte> payload,
        object? state,
        long nowTicks);

    private void LogSni(string domainName, IpPacket ipPacket)
    {
        if (SniEventId == null)
            return;

        VhLogger.Instance.LogInformation(SniEventId.Value,
            "Domain: {Domain}, DestEp: {IP}, Protocol: {Protocol}",
            VhLogger.FormatHostName(domainName), VhLogger.Format(ipPacket.DestinationAddress), ProtocolName);
    }

    private PacketFilterResult HandleSniFound(
        IpEndPointValue flowKey, IpPacket ipPacket, FlowInfo? flowInfo,
        string domainName, long nowTicks)
    {
        var action = DomainFilterResolver.Process(domainName);

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

        return new PacketFilterResult(action, domainName, packets, true);
    }

    private PacketFilterResult HandleNeedMore(
        IpEndPointValue flowKey, IpPacket ipPacket, FlowInfo? flowInfo,
        object sniState, long nowTicks)
    {
        // Buffer this packet and wait for more
        var state = flowInfo ?? new FlowInfo();
        state.SniState = sniState;
        state.BufferedPackets.Add(ipPacket);
        state.LastSeenTicks = nowTicks;
        _flowCache[flowKey] = state;

        return PacketFilterResult.Pending();
    }

    private PacketFilterResult HandleGiveUp(
        IpEndPointValue flowKey, FlowInfo? flowInfo, long nowTicks)
    {
        // Mark flow as decided (None = pass through)
        var state = flowInfo ?? new FlowInfo();
        state.Decision = DomainFilterAction.None;
        state.BufferedPackets = [];
        state.SniState = null;
        state.LastSeenTicks = nowTicks;
        _flowCache[flowKey] = state;

        // Release all buffered packets with None action
        return new PacketFilterResult(DomainFilterAction.None, null, flowInfo?.BufferedPackets, false);
    }

    private static void CleanupExpiredFlows(object? state)
    {
        if (state is not PacketSniService service || service._disposed)
            return;

        var nowTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
        var timeoutTicks = service.ConnectionTimeout.Ticks;

        foreach (var kvp in service._flowCache) {
            if (nowTicks - kvp.Value.LastSeenTicks <= timeoutTicks) 
                continue;

            if (!service._flowCache.TryRemove(kvp.Key, out var flowInfo)) 
                continue;
            
            // Dispose buffered packets
            foreach (var packet in flowInfo.BufferedPackets)
                packet.Dispose();
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
        GC.SuppressFinalize(this);
    }

    private class FlowInfo
    {
        public object? SniState { get; set; }
        public string? DomainName { get; set; }
        public DomainFilterAction? Decision { get; set; }
        public List<IpPacket> BufferedPackets { get; set; } = [];
        public long LastSeenTicks { get; set; }
    }
}
