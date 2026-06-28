using Microsoft.Extensions.Logging;
using VpnHood.Core.TcpStack.Abstractions;
using VpnHood.Core.Toolkit.Logging;
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

    /// <summary>Gets the aggregate number of bytes buffered across all connection reassembly pipes.</summary>
    public long TotalPipeBufferedBytes => Volatile.Read(ref _totalPipeBufferedBytes);

    /// <summary>Gets the window size of receive configured for this stack profile.</summary>
    public int ConfiguredReceiveWindow { get; internal set; }

    /// <summary>Gets the maximum number of simultaneous connections configured for this stack.</summary>
    public int ConfiguredMaxConnections { get; internal set; }

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
        if (VerboseLogging) {
            VhLogger.Instance.LogDebug(TcpStackEventIds.TcpStackDiag,
                "[TcpStack] +CONN established {EndPointPair} live={LiveEstablished}", endPointPair, live);
        }
    }

    /// <summary>
    /// Records that an established connection was released: drops the live established count and logs the
    /// event (with the teardown <paramref name="reason"/>) so TCP-stack stream teardown can be monitored via catlog.
    /// </summary>
    internal void DecrementEstablishedConnections(IPEndPointPairValue endPointPair, string reason)
    {
        var live = Interlocked.Decrement(ref _establishedConnections);
        if (VerboseLogging) {
            VhLogger.Instance.LogDebug(TcpStackEventIds.TcpStackDiag,
                "[TcpStack] -CONN released({Reason}) {EndPointPair} live={LiveEstablished}", reason, endPointPair, live);
        }
    }

    internal void AddPipeBufferedBytes(long bytes) => Interlocked.Add(ref _totalPipeBufferedBytes, bytes);

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
        VhLogger.Instance.LogTrace(TcpStackEventIds.TcpStackDiag,
            "[TcpStack] zero-win wait {EndPointPair} offset={Offset}/{DataLength} pw={PeerWindow} sndUna={SndUna} sndNxt={SndNxt} inFlight={InFlight}",
            endPointPair, offset, dataLength, peerWindow, sndUna, sndNxt, (long)(sndNxt - sndUna));
    }

    /// <summary>Traces a Zero Window Probe firing (throttled).</summary>
    internal void TraceZeroWindowProbe(IPEndPointPairValue endPointPair, int offset, uint peerWindow)
    {
        if (!VerboseLogging || !ShouldLog(ref _lastZwpLogTick)) return;
        VhLogger.Instance.LogTrace(TcpStackEventIds.TcpStackDiag,
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
        VhLogger.Instance.LogTrace(TcpStackEventIds.TcpStackDiag,
            "[ACK#{AckCount}] {EndPointPair} ack={Ack} prevUna={PrevUna} nxt={SndNxt} diff={Diff} winRaw={WindowSize} pw={PeerWindow} payload={PayloadLength} sig={WindowSignal} dup={DupAckCount}",
            ackCount, endPointPair, ack, prevUna, sndNxt, diff, windowSize, peerWindow, payloadLength, windowSignal, dupAckCount);
    }

    /// <summary>Traces a fast-retransmit event.</summary>
    internal void TraceFastRetransmit(IPEndPointPairValue endPointPair, long retxCount, uint sndUna, int retxBufferLen)
    {
        if (!VerboseLogging) return;
        VhLogger.Instance.LogTrace(TcpStackEventIds.TcpStackDiag,
            "[RETX#{RetxCount}] {EndPointPair} fast retransmit at sndUna={SndUna} retxLen={RetxBufferLen}",
            retxCount, endPointPair, sndUna, retxBufferLen);
    }
}
