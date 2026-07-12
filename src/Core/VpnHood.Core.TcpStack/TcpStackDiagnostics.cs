using Microsoft.Extensions.Logging;
using VpnHood.Core.TcpStack.Abstractions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Memory;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.TcpStack;

/// <summary>
/// Encapsulates live diagnostic metrics and performance counters for a <see cref="LocalTcpStack"/> instance.
/// All metrics are updated thread-safely.
/// </summary>
/// <remarks>
/// This type also owns the lifecycle logging for established connections: callers report the establish/release
/// events (via <see cref="IncrementEstablishedConnections"/> / <see cref="DecrementEstablishedConnections"/>)
/// and the counter + log line are produced together, so the running live count can never drift from what is
/// logged. Lines are at Debug level — grep catlog for "[TcpStack]" (or "+CONN" / "-CONN").
/// </remarks>
public sealed class TcpStackDiagnostics
{
    private int _connectionCount;
    private int _peakConnectionCount;
    private int _establishedConnections;
    private long _totalPipeBufferedBytes;

    /// <summary>Gets the number of concurrent connections currently tracked.</summary>
    public int ConnectionCount => Volatile.Read(ref _connectionCount);

    /// <summary>Gets the high-water mark of concurrent connections tracked during the stack's lifetime.</summary>
    public int PeakConnectionCount => Volatile.Read(ref _peakConnectionCount);

    /// <summary>Gets the number of tracked connections that have successfully established a handshake.</summary>
    public int EstablishedConnections => Volatile.Read(ref _establishedConnections);

    /// <summary>Gets the aggregate number of bytes buffered across all connection reassembly pipes.
    /// NOTE: this is a FLOW-CONTROL input, not just a metric — every connection's AdvertisedWindow
    /// subtracts it from <c>GlobalReceiveBudget</c> to compute the shared headroom. Removing or
    /// breaking its accounting silently disables the global budget of receive (iOS memory cap).</summary>
    public long TotalPipeBufferedBytes => Volatile.Read(ref _totalPipeBufferedBytes);

    /// <summary>Gets the window size of receive configured for this stack profile.</summary>
    public int ConfiguredReceiveWindow { get; internal set; }

    /// <summary>Gets the maximum number of simultaneous connections configured for this stack.</summary>
    public int ConfiguredMaxConnections { get; internal set; }

    // Fixed-width (5 chars, F1: "  8.3", " 43.2", "102.4") so the mem column stays aligned across
    // +CONN/-CONN lines when eyeballing the live device console. "  n/a" keeps the width. The footprint (the
    // number the host memory limit is enforced against — e.g. iOS jetsam phys_footprint) comes from the
    // device-set VhMemory.Instance; null on platforms that don't supply it → the column shows n/a.
    private static string FootprintFixed()
    {
        var mb = VhMemory.Instance.GetInfo().ProcessFootprintMb;
        return mb.HasValue ? $"{mb.Value,5:F1}" : "  n/a";
    }

    internal void SetConnectionCount(int count)
    {
        Volatile.Write(ref _connectionCount, count);

        int peak;
        do {
            peak = Volatile.Read(ref _peakConnectionCount);
            if (count <= peak) break;
        } while (Interlocked.CompareExchange(ref _peakConnectionCount, count, peak) != peak);
    }

    /// <summary>
    /// Records that a connection completed its handshake: bumps the live established count and logs the
    /// event so TCP-stack stream creation can be monitored via catlog.
    /// </summary>
    internal void IncrementEstablishedConnections(IPEndPointPairValue endPointPair)
    {
        var live = Interlocked.Increment(ref _establishedConnections);
        // Low-frequency lifecycle event (one per connection): logged at Debug and UNGATED so it's visible
        // when the log level is Debug without enabling the per-packet hot-path traces. Volume is
        // per-connection, not per-packet, so it never floods.
        // FORMAT: live/mem come FIRST (fixed-width: live 3 digits, mem 5 chars) so they stay in the
        // same on-screen columns in the live device console; the long endpoint pair goes last.
        VhLogger.Instance.LogDebug(TcpStackEventIds.TcpStack,
            "[TcpStack] +CONN live={LiveEstablished} mem={Memory}MB {EndPointPair}",
            live.ToString("D3"), FootprintFixed(), endPointPair);
    }

    /// <summary>
    /// Records that an established connection was released: drops the live established count and logs the
    /// event (with the teardown <paramref name="reason"/>) so TCP-stack stream teardown can be monitored via catlog.
    /// </summary>
    internal void DecrementEstablishedConnections(IPEndPointPairValue endPointPair, string reason)
    {
        var live = Interlocked.Decrement(ref _establishedConnections);
        // Low-frequency lifecycle event (one per connection): see IncrementEstablishedConnections — logged
        // at Debug, ungated, so it's visible at Debug level without the hot-path traces.
        // FORMAT: same fixed-width live/mem columns as +CONN; reason + endpoint trail at the end.
        VhLogger.Instance.LogDebug(TcpStackEventIds.TcpStack,
            "[TcpStack] -CONN live={LiveEstablished} mem={Memory}MB {EndPointPair} ({Reason})",
            live.ToString("D3"), FootprintFixed(), endPointPair, reason);
    }

    internal void AddPipeBufferedBytes(long bytes) => Interlocked.Add(ref _totalPipeBufferedBytes, bytes);

    // ---- memory admission gate -----------------------------------------------------------------
    private long _lastAdmissionLogTick;

    /// <summary>
    /// Records a SYN silently deferred by the memory admission gate. The log line is throttled to ~1/s
    /// because a browse burst can defer dozens of SYNs (plus their retransmits) in one pressure episode.
    /// </summary>
    internal void OnAdmissionDeferred(IPEndPointPairValue endPointPair, double footprintMb)
    {
        if (!ShouldLog(ref _lastAdmissionLogTick, 1000))
            return;
        VhLogger.Instance.LogDebug(TcpStackEventIds.TcpStack,
            "[TcpStack] admission deferred (mem={Memory}MB): dropping new SYN {EndPointPair} silently; peer will retry",
            footprintMb.ToString("F1"), endPointPair);
    }

    // --- Verbose data-path tracing -------------------------------------------------------------
    // All hot-path trace logging lives here so LocalTcpConnection/LocalTcpStack stay free of
    // formatting, gating and throttling concerns. Each method is a no-op (a single bool read) when
    // VerboseLogging is off, so leaving the call sites in the data path costs nothing in production.
    // Throttling is per-stack (this object is one-per-stack); messages carry the endpoint pair so
    // they remain attributable across connections.

    private long _lastZeroWinLogTick;
    private long _lastZwpLogTick;

    /// <summary>
    /// Gets or sets whether verbose data-path trace logging is emitted. Mirrors
    /// <c>LocalTcpStack.VerboseLogging</c> (the stack forwards to this single flag).
    /// </summary>
    public bool VerboseLogging { get; set; }

    // Best-effort throttle: a race between threads may let two lines through, which is fine for a trace.
    private static bool ShouldLog(ref long lastTick, int minIntervalMs = 500)
    {
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref lastTick) <= minIntervalMs) return false;
        Interlocked.Exchange(ref lastTick, now);
        return true;
    }

    /// <summary>Traces a sender parked on a (near-)zero peer window (throttled).</summary>
    internal void TraceZeroWindowWait(IPEndPointPairValue endPointPair, int offset, int dataLength,
        uint peerWindow, uint sndUna, uint sndNxt)
    {
        if (!VerboseLogging || !ShouldLog(ref _lastZeroWinLogTick)) return;
        VhLogger.Instance.LogTrace(TcpStackEventIds.TcpStack,
            "[TcpStack] zero-win wait {EndPointPair} offset={Offset}/{DataLength} pw={PeerWindow} sndUna={SndUna} sndNxt={SndNxt} inFlight={InFlight}",
            endPointPair, offset, dataLength, peerWindow, sndUna, sndNxt, (long)(sndNxt - sndUna));
    }

    /// <summary>Traces a Zero Window Probe firing (throttled).</summary>
    internal void TraceZeroWindowProbe(IPEndPointPairValue endPointPair, int offset, uint peerWindow)
    {
        if (!VerboseLogging || !ShouldLog(ref _lastZwpLogTick)) return;
        VhLogger.Instance.LogTrace(TcpStackEventIds.TcpStack,
            "[TcpStack] ZWP fire {EndPointPair} offset={Offset} pw={PeerWindow}", endPointPair, offset, peerWindow);
    }

    /// <summary>
    /// Traces a processed ACK, but only for significant events (zero/low advertised window, a
    /// non-advancing payload-less ACK, or a periodic heartbeat) to avoid log spam.
    /// </summary>
    internal void TraceAck(IPEndPointPairValue endPointPair, int ackCount, uint ack, uint prevUna,
        uint sndNxt, long diff, ushort windowSize, uint peerWindow, int payloadLength,
        int windowSignal, int dupAckCount)
    {
        if (!VerboseLogging) return;
        var significant = windowSize == 0 || peerWindow < 4096 ||
                          (diff <= 0 && payloadLength == 0) || ackCount % 5000 == 0;
        if (!significant) return;
        VhLogger.Instance.LogTrace(TcpStackEventIds.TcpStack,
            "[ACK#{AckCount}] {EndPointPair} ack={Ack} prevUna={PrevUna} nxt={SndNxt} diff={Diff} winRaw={WindowSize} pw={PeerWindow} payload={PayloadLength} sig={WindowSignal} dup={DupAckCount}",
            ackCount, endPointPair, ack, prevUna, sndNxt, diff, windowSize, peerWindow, payloadLength, windowSignal, dupAckCount);
    }

    /// <summary>Traces a fast-retransmit event.</summary>
    internal void TraceFastRetransmit(IPEndPointPairValue endPointPair, long retxCount, uint sndUna, int retxBufferLen)
    {
        if (!VerboseLogging) return;
        VhLogger.Instance.LogTrace(TcpStackEventIds.TcpStack,
            "[RETX#{RetxCount}] {EndPointPair} fast retransmit at sndUna={SndUna} retxLen={RetxBufferLen}",
            retxCount, endPointPair, sndUna, retxBufferLen);
    }
}
