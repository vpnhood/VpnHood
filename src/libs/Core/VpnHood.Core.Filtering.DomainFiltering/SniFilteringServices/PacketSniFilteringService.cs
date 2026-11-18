using Microsoft.Extensions.Logging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering.SniExtractors;
using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Filtering.DomainFiltering.SniFilteringServices;

/// <summary>
/// Base class for protocol-specific SNI extraction services.
/// Handles flow management, packet buffering, and domain filtering.
/// </summary>
internal abstract class PacketSniFilteringService(
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

        // Check if we already have a decision for this flow; another connection begun on it has its name read anew, and
        // the flow takes its verdict: kept, the verdict of the name before answered a name never read
        if (_flowCacheService.TryGetValue(flowKey, out var flowInfo) && flowInfo.Decision != null) {
            if (IsNewFlow(payload, flowInfo, nowTicks)) {
                _flowCacheService.TryRemove(flowKey, out _);
                flowInfo = null;
            }
            else {
                // Remove the flow if end-of-flow signal is detected (e.g., TCP FIN/RST)
                if (IsFlowEnd(ipPacket))
                    _flowCacheService.TryRemove(flowKey, out _);
                else
                    flowInfo.LastSeenTicks = nowTicks;

                return new PacketSniFilterResult(flowInfo.Decision.Value, flowInfo.DomainName, null, false);
            }
        }

        // A flow that ends with no data before its name is found keeps nothing for the next flow of the same key. What
        // it held is a ClientHello begun and never complete, which fails closed, as one late does; with nothing held,
        // the end goes as no objection
        if (payload.IsEmpty && IsFlowEnd(ipPacket)) {
            _flowCacheService.TryRemove(flowKey, out _);
            var action = flowInfo?.BufferedPackets.Count > 0 ? FilterAction.Block : FilterAction.Default;
            return new PacketSniFilterResult(action, null, flowInfo?.BufferedPackets, false);
        }

        // Extract SNI from payload
        var sniResult = ExtractSni(payload, flowInfo?.SniState, nowTicks);

        // A flow that ends with this packet keeps nothing for the next flow of the same key, and a ClientHello still
        // not complete then never will be
        var isEnd = IsFlowEnd(ipPacket);
        PacketSniFilterResult result;

        // SNI found
        if (sniResult.DomainName != null) {
            LogSni(sniResult.DomainName, ipPacket);
            result = HandleSniFound(flowKey, flowInfo, sniResult.DomainName, sniResult.ConnectionId, nowTicks);
        }

        // Need more packets to extract SNI
        else if (sniResult is { NeedMore: true, State: not null } && !isEnd)
            return HandleNeedMore(flowKey, ipPacket, flowInfo, sniResult.State, nowTicks);

        // A name begun and not complete in time: let through, a lost or late part would pass a block, or take an
        // included name out of the tunnel, so the flow is dropped, as a TCP connection's is (StreamSniExtractor)
        else if (sniResult.IsIncomplete || sniResult.NeedMore) {
            VhLogger.Instance.LogDebug(SniEventId ?? default,
                "A {Protocol} ClientHello was not complete in time, so its flow is dropped. DestEp: {IP}",
                ProtocolName, VhLogger.Format(ipPacket.DestinationAddress));
            result = HandleDecided(flowKey, flowInfo, FilterAction.Block, sniResult.ConnectionId, nowTicks);
        }

        // Could not extract SNI - release all buffered packets
        else
            result = HandleDecided(flowKey, flowInfo, FilterAction.Default, sniResult.ConnectionId, nowTicks);

        if (isEnd)
            _flowCacheService.TryRemove(flowKey, out _);

        return result;
    }

    /// <summary>
    /// Validate the packet and extract the flow key and payload.
    /// </summary>
    protected abstract bool TryValidateAndExtractPayload(
        IpPacket ipPacket,
        out FlowKey flowKey,
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

    /// <summary>
    /// Check if a packet of a decided flow begins another connection on the same addresses and ports, whose name is to
    /// be read anew. Asked of every such packet, so it must be cheap for most.
    /// </summary>
    protected virtual bool IsNewFlow(ReadOnlySpan<byte> payload, FlowInfo flowInfo, long nowTicks) => false;

    private void LogSni(string domainName, IpPacket ipPacket)
    {
        if (SniEventId == null)
            return;

        VhLogger.Instance.LogInformation(SniEventId.Value,
            "Domain: {Domain}, DestEp: {IP}, Protocol: {Protocol}",
            VhLogger.FormatHostName(domainName), VhLogger.Format(ipPacket.DestinationAddress), ProtocolName);
    }

    private PacketSniFilterResult HandleSniFound(
        FlowKey flowKey, FlowInfo? flowInfo,
        string domainName, byte[]? connectionId, long nowTicks)
    {
        var action = DomainFilter.Process(domainName);

        // Collect all previous buffered
        var packets = flowInfo?.BufferedPackets ?? [];

        // Update state with decision for future packets in this flow
        var state = flowInfo ?? new FlowInfo();
        state.DomainName = domainName;
        state.Decision = action;
        state.ConnectionId = connectionId;
        state.BufferedPackets = []; // Clear buffer since we're releasing them
        state.SniState = null;
        state.LastSeenTicks = nowTicks;
        _flowCacheService.Set(flowKey, state);

        return new PacketSniFilterResult(action, domainName, packets, true);
    }

    private PacketSniFilterResult HandleNeedMore(
        FlowKey flowKey, IpPacket ipPacket, FlowInfo? flowInfo,
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

    // A flow decided with no name: Default, as no objection, for one that holds none, or Block for one whose name did
    // not come in time
    private PacketSniFilterResult HandleDecided(
        FlowKey flowKey, FlowInfo? flowInfo, FilterAction action, byte[]? connectionId, long nowTicks)
    {
        // Collect all previous buffered first: the state below is the same flow's, and takes a new list
        var packets = flowInfo?.BufferedPackets;

        // Mark flow as decided
        var state = flowInfo ?? new FlowInfo();
        state.Decision = action;
        state.ConnectionId = connectionId;
        state.BufferedPackets = [];
        state.SniState = null;
        state.LastSeenTicks = nowTicks;
        _flowCacheService.Set(flowKey, state);

        // Release all buffered packets with the flow's action
        return new PacketSniFilterResult(action, null, packets, false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        _flowCacheService.Dispose();
        GC.SuppressFinalize(this);
    }
}
