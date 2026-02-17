using Microsoft.Extensions.Logging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering.SniExtractors;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Filtering.DomainFiltering.SniFilteringServices;

/// <summary>
/// Base class for protocol-specific SNI extraction services.
/// Handles flow management, packet buffering, and domain filtering.
/// </summary>
public abstract class PacketSniFilteringService(
    IDomainFilter domainFilter,
    TimeSpan flowTimeout,
    EventId? sniEventId)
    : IDisposable
{
    private readonly FlowCacheService _flowCacheService = new(flowTimeout);
    private bool _disposed;
    protected IDomainFilter DomainFilter { get; } = domainFilter;
    protected EventId? SniEventId { get; } = sniEventId;
    protected abstract string ProtocolName { get; }

    /// <summary>
    /// Process an IP packet for SNI extraction and domain filtering.
    /// </summary>
    public PacketSniFilterResult ProcessPacket(IpPacket ipPacket)
    {
        ObjectDisposedException.ThrowIf(_disposed, GetType().Name);

        // Validate packet protocol and get payload
        if (!TryValidateAndExtractPayload(ipPacket, out var flowKey, out var payload))
            return PacketSniFilterResult.Passthrough();

        var nowTicks = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;

        // Check if we already have a decision for this flow
        if (_flowCacheService.TryGetValue(flowKey, out var flowInfo) && flowInfo.Decision != null) {
            // Remove the flow if end-of-flow signal is detected (e.g., TCP FIN/RST)
            if (IsFlowEnd(ipPacket))
                _flowCacheService.TryRemove(flowKey, out _);
            else
                flowInfo.LastSeenTicks = nowTicks;

            return new PacketSniFilterResult(flowInfo.Decision.Value, flowInfo.DomainName, null, false);
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
    protected abstract PacketSniResult ExtractSni(
        ReadOnlySpan<byte> payload,
        object? state,
        long nowTicks);

    /// <summary>
    /// Check if the packet signals the end of a flow (e.g., TCP FIN or RST).
    /// </summary>
    protected virtual bool IsFlowEnd(IpPacket ipPacket) => false;

    private void LogSni(string domainName, IpPacket ipPacket)
    {
        if (SniEventId == null)
            return;

        VhLogger.Instance.LogInformation(SniEventId.Value,
            "Domain: {Domain}, DestEp: {IP}, Protocol: {Protocol}",
            VhLogger.FormatHostName(domainName), VhLogger.Format(ipPacket.DestinationAddress), ProtocolName);
    }

    private PacketSniFilterResult HandleSniFound(
        IpEndPointValue flowKey, IpPacket ipPacket, FlowInfo? flowInfo,
        string domainName, long nowTicks)
    {
        var action = DomainFilter.Process(domainName);

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
        _flowCacheService.Set(flowKey, state);

        return new PacketSniFilterResult(action, domainName, packets, true);
    }

    private PacketSniFilterResult HandleNeedMore(
        IpEndPointValue flowKey, IpPacket ipPacket, FlowInfo? flowInfo,
        object sniState, long nowTicks)
    {
        // Buffer this packet and wait for more
        var state = flowInfo ?? new FlowInfo();
        state.SniState = sniState;
        state.BufferedPackets.Add(ipPacket);
        state.LastSeenTicks = nowTicks;
        _flowCacheService.Set(flowKey, state);

        return PacketSniFilterResult.Pending();
    }

    private PacketSniFilterResult HandleGiveUp(
        IpEndPointValue flowKey, FlowInfo? flowInfo, long nowTicks)
    {
        // Mark flow as decided (None = pass through)
        var state = flowInfo ?? new FlowInfo();
        state.Decision = FilterAction.Default;
        state.BufferedPackets = [];
        state.SniState = null;
        state.LastSeenTicks = nowTicks;
        _flowCacheService.Set(flowKey, state);

        // Release all buffered packets with None action
        return new PacketSniFilterResult(FilterAction.Default, null, flowInfo?.BufferedPackets, false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _flowCacheService.Dispose();
        GC.SuppressFinalize(this);
    }
}
