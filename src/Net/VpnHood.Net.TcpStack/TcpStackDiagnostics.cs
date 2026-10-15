using Microsoft.Extensions.Logging;
using VpnHood.Net.TcpStack.Abstractions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Memory;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.TcpStack;

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
    private readonly LocalTcpStack _stack;
    private int _connectionCount;
    private int _peakConnectionCount;
    private int _establishedConnections;

    internal TcpStackDiagnostics(LocalTcpStack stack)
    {
        _stack = stack;
    }

    /// <summary>Gets the number of concurrent connections currently tracked.</summary>
    public int ConnectionCount => Volatile.Read(ref _connectionCount);

    /// <summary>Gets the high-water mark of concurrent connections tracked during the stack's lifetime.</summary>
    public int PeakConnectionCount => Volatile.Read(ref _peakConnectionCount);

    /// <summary>Gets the number of tracked connections that have successfully established a handshake.</summary>
    public int EstablishedConnections => Volatile.Read(ref _establishedConnections);

    /// <summary>Gets the bytes buffered across all connection reassembly pipes, summed on read: a diagnostic (the iOS
    /// memory monitor logs it), no flow-control input, nothing counted per packet.</summary>
    public long TotalPipeBufferedBytes => _stack.PipeBufferedBytes;

    /// <summary>Gets the window size of receive configured for this stack profile.</summary>
    public int ConfiguredReceiveWindow { get; internal set; }

    /// <summary>Gets the maximum number of simultaneous connections configured for this stack.</summary>
    public int ConfiguredMaxConnections { get; internal set; }

    // Logs one connection-lifecycle line: "[TcpStack] ±CONN live=NNN mem=MM.MMB detail". Fixed-width
    // live/mem columns come FIRST (live 3 digits, mem 5 chars) so they stay in the same on-screen columns
    // in the live device console; the long detail (endpoint pair) goes last. IosQuicDiagnostics.LogConn
    // duplicates this shape on purpose (kept in sync by hand — no shared helper) so TcpStack and QUIC
    // lifecycle lines stay column-aligned when interleaved.
    private static void LogConn(string evt, int live, object detail)
    {
        VhLogger.Instance.LogDebug(TcpStackEventIds.TcpStack,
            "[TcpStack] {Event} live={Live} mem={Memory}MB {Detail}",
            evt, live.ToString("D3"), FootprintFixed(), detail);
    }

    // Fixed-width (5 chars, F1: "  8.3", " 43.2", "102.4") so the mem column stays aligned across
    // +CONN/-CONN lines when eyeballing the live device console. "  n/a" keeps the width. The footprint (the
    // number the host memory limit is enforced against — e.g. iOS jetsam phys_footprint) comes from the
    // device-set VhMemory.Instance; null on platforms that don't supply it → the column shows n/a.
    private static string FootprintFixed()
    {
        var mb = VhMemory.Instance.GetInfo().ProcessFootprintMb;
        return mb.HasValue ? $"{mb.Value,5:F1}" : "  n/a";
    }

    /// <summary>Adds to the connection count (+1 on a SYN admitted with no cap, -1 on a close or a slot given back) and
    /// returns the new count; the peak follows. The stack's cap takes its slots from this count too
    /// (<see cref="TryReserveConnection"/>), so the two can never drift apart.</summary>
    internal int AddConnections(int delta)
    {
        var count = Interlocked.Add(ref _connectionCount, delta);
        RaisePeak(count);
        return count;
    }

    /// <summary>Takes one connection slot while the count is below <paramref name="max"/>: the test and the add are one
    /// atomic step, so SYNs racing at the cap cannot all pass it, and a refused one leaves the count and its peak as
    /// they were. <paramref name="count"/> is the new count, or the full one.</summary>
    internal bool TryReserveConnection(int max, out int count)
    {
        count = Volatile.Read(ref _connectionCount);
        while (count < max) {
            var seen = Interlocked.CompareExchange(ref _connectionCount, count + 1, count);
            if (seen == count) {
                count++;
                RaisePeak(count);
                return true;
            }
            count = seen;
        }
        return false;
    }

    private void RaisePeak(int count)
    {
        var peak = Volatile.Read(ref _peakConnectionCount);
        while (count > peak) {
            var seen = Interlocked.CompareExchange(ref _peakConnectionCount, count, peak);
            if (seen == peak) break;
            peak = seen;
        }
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
        LogConn("+CONN", live, endPointPair);
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
        LogConn("-CONN", live, $"{endPointPair} ({reason})");
    }

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

    // ---- handshakes given up ---------------------------------------------------------------------
    private long _lastHandshakeLogTick;

    /// <summary>
    /// Records a handshake given up: no ACK came for our SYN-ACKs, which a kernel answers at once, so the path to it is
    /// broken or the SYN was not a kernel's. Throttled to ~1/s, as a SYN flood gives many up together.
    /// </summary>
    internal void OnHandshakeGivenUp(IPEndPointPairValue endPointPair)
    {
        if (!ShouldLog(ref _lastHandshakeLogTick, 1000))
            return;
        VhLogger.Instance.LogDebug(TcpStackEventIds.TcpStack,
            "[TcpStack] handshake given up: no ACK of our SYN-ACKs from {EndPointPair}", endPointPair);
    }

    // ---- failures ------------------------------------------------------------------------------
    private long _lastFailureLogTick;
    private int _suppressedFailures;

    /// <summary>
    /// Logs an exception that escaped the receive path or a sweep, which went on without it. Nothing expected
    /// throws there, so it is an error. One line per second, as a failure may repeat per packet: the line counts
    /// the rest.
    /// </summary>
    internal void LogFailure(string where, Exception ex)
    {
        if (!ShouldLog(ref _lastFailureLogTick, 1000)) {
            Interlocked.Increment(ref _suppressedFailures);
            return;
        }

        VhLogger.Instance.LogError(TcpStackEventIds.TcpStack, ex,
            "[TcpStack] {Where} failed; {Suppressed} more since the last line",
            where, Interlocked.Exchange(ref _suppressedFailures, 0));
    }

    // --- Verbose data-path tracing -------------------------------------------------------------
    // All hot-path trace logging lives here so LocalTcpConnection/LocalTcpStack stay free of
    // formatting, gating and throttling concerns. The hot call sites check VerboseLogging first, so a
    // disabled trace costs one bool read there; each method checks it again for its other callers.
    // Throttling is per-stack (this object is one-per-stack); messages carry the endpoint pair so
    // they remain attributable across connections.

    private long _lastZeroWinLogTick;
    private long _lastZwpLogTick;

    /// <summary>
    /// Gets or sets whether verbose data-path trace logging is emitted. Mirrors
    /// <c>LocalTcpStack.VerboseLogging</c> (the stack forwards to this single flag).
    /// </summary>
    public bool VerboseLogging { get; set; }

    // Best-effort throttle: a race between threads may let two lines through, which is fine for a log line.
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
