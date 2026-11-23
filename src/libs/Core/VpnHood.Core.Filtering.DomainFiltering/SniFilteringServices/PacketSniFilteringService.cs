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
/// <remarks>
/// A flow is the packets between two addresses and ports, and has one verdict, which answers every packet that begins
/// no connection. A protocol that names its connections, as QUIC does, may carry more than one on a flow, at once or
/// one after another: each has its own name read from its first packets, which are held meanwhile and leave with its
/// own verdict. A blocked one is dropped there, at its first packets, and never gets past them, so its block does not
/// end the flow's other connections. Past their first packets the connections of a flow are not told apart, so the
/// flow's verdict is the most protective of theirs: none leaves the tunnel by another's exclude.
/// </remarks>
internal abstract class PacketSniFilteringService(
    IDomainFilter domainFilter,
    TimeSpan flowTimeout,
    EventId? sniEventId)
    : IDisposable
{
    // The most packets a read holds: a ClientHello's own, sixteen at most for QUIC, with the early data a client may
    // send beside them. A read past it ends as one whose time is out, or a flow whose name never comes would hold
    // whatever its app sends until then
    private const int MaxHeldPackets = 64;

    private readonly FlowCacheService _flowCacheService = new(flowTimeout);
    private bool _disposed;
    protected IDomainFilter DomainFilter { get; } = domainFilter;
    protected EventId? SniEventId { get; } = sniEventId;
    protected abstract string ProtocolName { get; }

    /// <summary>What a packet is to the connections of its flow.</summary>
    protected enum FlowPacketKind
    {
        /// <summary>
        /// It begins no connection: the flow's verdict answers it, and where the flow has none yet, it waits for it
        /// with the packets of the read.
        /// </summary>
        Other,

        /// <summary>It belongs to the connection whose name is being read.</summary>
        ReadPacket,

        /// <summary>
        /// A packet of the connection read last to its end that is known as its own: a first packet sent again, or
        /// early data.
        /// </summary>
        KnownConnection,

        /// <summary>A first packet of another connection, whose name is to be read.</summary>
        ConnectionStart
    }

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
        _flowCacheService.RemoveExpired(nowTicks);

        // the state of the read this packet goes on with, if any
        object? readState = null;
        if (_flowCacheService.TryGetValue(flowKey, out var flowInfo)) {
            var packetKind = GetPacketKind(payload, flowInfo, nowTicks);

            // A packet that begins no connection takes the flow's verdict, and one known as the connection's read
            // last, as a first packet sent again, takes what its first packets took: a blocked one's are dropped
            // each time
            if (flowInfo.Decision != null && packetKind is FlowPacketKind.Other or FlowPacketKind.KnownConnection) {
                // Remove the flow if end-of-flow signal is detected (e.g., TCP FIN/RST)
                if (IsFlowEnd(ipPacket))
                    _flowCacheService.TryRemove(flowKey, out _);
                else
                    flowInfo.LastSeenTicks = nowTicks;

                var packetAction = packetKind is FlowPacketKind.KnownConnection
                    ? flowInfo.ConnectionAction
                    : flowInfo.Decision.Value;
                return new PacketSniFilterResult(packetAction,
                    packetAction == FilterAction.Block ? null : flowInfo.DomainName, null, false);
            }

            // Another connection begins while one is read. Its packets cannot wait with the other's, whose verdict
            // they would leave with, so it takes the read, and what was held is given up: its client sends it again
            if (packetKind == FlowPacketKind.ConnectionStart) {
                flowInfo.SniState = null;
                flowInfo.BufferedPackets = [];
            }

            // The read's own packet, or any packet of a flow that has no verdict yet, which waits for it with the
            // read's: it goes through the reader too, whose time limit ends the wait
            else
                readState = flowInfo.SniState;
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
        var sniResult = ExtractSni(payload, readState, nowTicks);

        // A flow that ends with this packet keeps nothing for the next flow of the same key, and a ClientHello still
        // not complete then never will be
        var isEnd = IsFlowEnd(ipPacket);

        // Need more packets to extract SNI, while the read may hold more
        if (sniResult is { NeedMore: true, State: not null } && !isEnd &&
            !(flowInfo?.BufferedPackets.Count >= MaxHeldPackets))
            return HandleNeedMore(flowKey, ipPacket, flowInfo, sniResult.State, nowTicks);

        // The read is over, and the connection has its own verdict: its name's; Block for a name that could not be
        // read, as one begun and not complete in time, since a lost or late part let through would pass a block, or
        // take an included name out of the tunnel, so it is dropped, as a TCP connection's is (StreamSniExtractor); or
        // Default, as no objection, for no name. Where the filter holds no rule, as when names are read for the log
        // alone, no name could change the verdict, so a name not read drops nothing
        var isIncomplete = sniResult.IsIncomplete || sniResult.NeedMore;
        FilterAction connectionAction;
        if (sniResult.DomainName != null) {
            LogSni(sniResult.DomainName, ipPacket);
            connectionAction = DomainFilter.Process(sniResult.DomainName);
        }
        else if (isIncomplete && !DomainFilter.IsEmpty) {
            VhLogger.Instance.LogDebug(SniEventId ?? default,
                "The name of a {Protocol} ClientHello could not be read, so its packets are dropped. DestEp: {IP}",
                ProtocolName, VhLogger.Format(ipPacket.DestinationAddress));
            connectionAction = FilterAction.Block;
        }
        else
            connectionAction = FilterAction.Default;

        var result = HandleReadEnd(flowKey, flowInfo, connectionAction, sniResult.DomainName,
            sniResult.ConnectionId, isIncomplete, nowTicks);

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
    /// Tells what a packet of a known flow is to the flow's connections. Asked of every such packet, so it must be
    /// cheap for most.
    /// </summary>
    /// <remarks>
    /// A protocol that names no connection has one on a flow: its packets are the read's until the flow is decided, and
    /// the flow's after.
    /// </remarks>
    protected virtual FlowPacketKind GetPacketKind(ReadOnlySpan<byte> payload, FlowInfo flowInfo, long nowTicks) =>
        flowInfo.Decision != null ? FlowPacketKind.Other : FlowPacketKind.ReadPacket;

    private void LogSni(string domainName, IpPacket ipPacket)
    {
        if (SniEventId == null)
            return;

        VhLogger.Instance.LogInformation(SniEventId.Value,
            "Domain: {Domain}, DestEp: {IP}, Protocol: {Protocol}",
            VhLogger.FormatHostName(domainName), VhLogger.Format(ipPacket.DestinationAddress), ProtocolName);
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

    // A read's end: the packets it held leave with the connection's verdict, which joins the flow's
    private PacketSniFilterResult HandleReadEnd(
        FlowKey flowKey, FlowInfo? flowInfo, FilterAction connectionAction, string? domainName,
        byte[]? connectionId, bool isIncomplete, long nowTicks)
    {
        // Collect all previous buffered first: the state below is the same flow's, and takes a new list
        var packets = flowInfo?.BufferedPackets;
        var state = flowInfo ?? new FlowInfo();

        // The flow's first connection gives the flow its verdict. A later one that is blocked leaves it as it is, and
        // is dropped here, at its first packets; any other makes it the more protective of the two.
        // The connection's first packets leave with its own verdict, so a name with no entry is still judged by the IP
        // rules, whose block holds it here as a domain block would, whatever the flow's verdict. An excluded one alone
        // takes the flow's from its first packet: its later packets will, and a connection that goes two ways fails
        var action = connectionAction;
        if (state.Decision == null) {
            state.Decision = connectionAction;
            state.DomainName = domainName;
        }
        else if (connectionAction != FilterAction.Block) {
            state.Decision = GetMoreProtective(state.Decision.Value, connectionAction);
            state.DomainName = domainName ?? state.DomainName;
            if (connectionAction == FilterAction.Exclude)
                action = state.Decision.Value;
        }

        // A connection read to its end is known by its ID from now on. One not complete is not, so its first packets
        // sent again are read again: a part that was late costs the connection one resend, not itself
        if (!isIncomplete) {
            state.ConnectionId = connectionId;
            state.ConnectionAction = action;
        }

        state.BufferedPackets = [];
        state.SniState = null;
        state.LastSeenTicks = nowTicks;
        _flowCacheService.Set(flowKey, state);

        return new PacketSniFilterResult(action, domainName, packets, isNewFlow: domainName != null);
    }

    // The verdict of a flow that carries a connection of each verdict. Once past their first packets its connections
    // are not told apart, so one's packets may get the other's verdict: an include keeps both in the tunnel, and with
    // no include, a default leaves both to the IP rules; only two excludes go around. A block gives way, as a blocked
    // connection is dropped at its first packets and has none after
    private static FilterAction GetMoreProtective(FilterAction flowAction, FilterAction connectionAction)
    {
        if (flowAction == FilterAction.Block)
            return connectionAction;

        if (flowAction == FilterAction.Include || connectionAction == FilterAction.Include)
            return FilterAction.Include;

        return flowAction == FilterAction.Default || connectionAction == FilterAction.Default
            ? FilterAction.Default
            : FilterAction.Exclude;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        _flowCacheService.Dispose();
        GC.SuppressFinalize(this);
    }
}
