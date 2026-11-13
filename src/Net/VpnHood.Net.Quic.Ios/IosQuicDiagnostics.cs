using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Memory;

namespace VpnHood.Net.Quic.Ios;

/// <summary>
/// Provides the iOS QUIC client's investigation counters and its <c>[VHQUIC]</c> open and close log lines, as
/// <c>TcpStackDiagnostics</c> does for the TCP stack. A stream, a connection, a send or a teardown is counted only when
/// it begins while <see cref="Enabled"/>, so production pays nothing; its end is then counted whatever the log level is
/// by then, so no count goes below zero or stays up.
/// </summary>
/// <remarks>
/// Only the diagnostic counters live here. The load-bearing jetsam brake input is deliberately NOT gated: the
/// live phys_footprint is published always-on through <c>VhMemory.Instance</c> by the iOS footprint
/// sampler, not by this class. The host's memory probe reads the public snapshot properties below; it does not
/// own these counters.
/// <para>Off in production; there is no dedicated switch — <see cref="Enabled"/> is computed from
/// <c>VhLogger.MinLogLevel</c>, so below-Information logging (e.g. the <c>/log:debug</c> DebugCommand in
/// the app UI, flowing to the Extension via <c>ClientOptions.LogServiceOptions</c>) enables all the iOS
/// diagnostics together.</para>
/// </remarks>
public static class IosQuicDiagnostics
{
    // ---- backing fields --------------------------------------------------------------------------
    private static int _streamSeq;
    private static int _liveStreamCount;
    private static int _connectionSeq;
    private static int _liveConnectionCount;
    private static long _outstandingSendBytes;
    private static long _maxStreamCancelMs;

    // ---- public state ----------------------------------------------------------------------------
    /// <summary>
    /// Gets a value indicating whether the counters move and the lines are logged: while the log level is below
    /// Information.
    /// </summary>
    /// <remarks>
    /// What was open, or being sent, before the log level dropped is never counted, so the counts may then be under
    /// the real ones; they are exact for everything that began since.
    /// </remarks>
    public static bool Enabled => VhLogger.MinLogLevel < LogLevel.Information;

    /// <summary>Gets the number of open QUIC streams, each a native NWConnection.</summary>
    public static int LiveStreamCount => Volatile.Read(ref _liveStreamCount);
    /// <summary>Gets the number of open QUIC connections, each a native connection group.</summary>
    public static int LiveConnectionCount => Volatile.Read(ref _liveConnectionCount);
    /// <summary>
    /// Gets the bytes of the sends under way: counted just before a send goes to <c>nw_connection_send</c>, until its
    /// completion callback, a failure before it is submitted, or the stream's disposal.
    /// </summary>
    public static long OutstandingSendBytes => Interlocked.Read(ref _outstandingSendBytes);

    // ---- stream lifecycle ------------------------------------------------------------------------
    /// <summary>
    /// Records a QUIC stream open (logs <c>[VHQUIC] +CONN</c>) and returns the assigned monotonic id so
    /// the matching close can be paired in the log. Returns 0 (and logs nothing) when disabled.
    /// </summary>
    internal static int OnStreamOpened()
    {
        if (!Enabled) return 0;
        var id = Interlocked.Increment(ref _streamSeq);
        var live = Interlocked.Increment(ref _liveStreamCount);
        LogConn("+CONN", live, id);
        return id;
    }

    /// <summary>
    /// Records the close of a QUIC stream whose open was counted (logs <c>[VHQUIC] -CONN</c>). No-op for id 0, the
    /// open that was not counted.
    /// </summary>
    internal static void OnStreamClosed(int id)
    {
        if (id == 0) return;
        var live = Interlocked.Decrement(ref _liveStreamCount);
        if (Enabled)
            LogConn("-CONN", live, id);
    }

    /// <summary>Records creation of an underlying QUIC connection group.</summary>
    internal static int OnConnectionOpened()
    {
        if (!Enabled) return 0;
        var id = Interlocked.Increment(ref _connectionSeq);
        var live = Interlocked.Increment(ref _liveConnectionCount);
        VhLogger.Instance.LogDebug(IosQuicEventIds.Quic,
            "[VHQUIC] +GROUP live={Live} mem={Memory}MB id={Id}",
            live.ToString("D3"), FootprintFixed(), id);
        return id;
    }

    /// <summary>
    /// Records disposal of an underlying QUIC connection group whose creation was counted. No-op for id 0, the
    /// creation that was not counted.
    /// </summary>
    internal static void OnConnectionClosed(int id)
    {
        if (id == 0) return;
        var live = Interlocked.Decrement(ref _liveConnectionCount);
        if (Enabled)
            VhLogger.Instance.LogDebug(IosQuicEventIds.Quic,
                "[VHQUIC] -GROUP live={Live} mem={Memory}MB id={Id}",
                live.ToString("D3"), FootprintFixed(), id);
    }

    // Logs one stream-lifecycle line: "[VHQUIC] ±CONN live=NNN mem=MM.MMB id=N". Fixed-width live/mem
    // columns come FIRST (live 3 digits, mem 5 chars) and the stream id (QUIC's analogue of the endpoint
    // pair) trails. TcpStackDiagnostics.LogConn duplicates this shape on purpose (kept in sync by hand —
    // no shared helper) so TcpStack and QUIC lifecycle lines stay column-aligned when interleaved.
    private static void LogConn(string evt, int live, int id)
    {
        VhLogger.Instance.LogDebug(IosQuicEventIds.Quic,
            "[VHQUIC] {Event} live={Live} mem={Memory}MB id={Id}",
            evt, live.ToString("D3"), FootprintFixed(), id);
    }

    // Fixed-width (5 chars, F1: "  8.3", " 43.2", "102.4") so the mem column stays aligned across
    // +CONN/-CONN lines when eyeballing the live device console. "  n/a" keeps the width. The footprint (the
    // number the host memory limit is enforced against — iOS jetsam phys_footprint) comes from the
    // device-set VhMemory.Instance; null on platforms that don't supply it → the column shows n/a.
    private static string FootprintFixed()
    {
        var mb = VhMemory.Instance.GetInfo().ProcessFootprintMb;
        return mb.HasValue ? $"{mb.Value,5:F1}" : "  n/a";
    }

    // ---- teardown timing -------------------------------------------------------------------------
    /// <summary>
    /// Returns a start timestamp for bracketing the native stream teardown, or 0 when disabled so the
    /// paired <see cref="EndStreamTeardown"/> becomes a no-op.
    /// </summary>
    internal static long BeginTiming() => Enabled ? Environment.TickCount64 : 0;

    /// <summary>
    /// Records a completed QUIC stream teardown, tracking the worst duration. No-op for a start of 0, the teardown
    /// that was not timed from its beginning: its duration would read as the device's uptime.
    /// </summary>
    internal static void EndStreamTeardown(long startTimestamp)
    {
        if (startTimestamp == 0) return;
        var elapsed = Environment.TickCount64 - startTimestamp;
        long prev;
        while (elapsed > (prev = Volatile.Read(ref _maxStreamCancelMs)) &&
               Interlocked.CompareExchange(ref _maxStreamCancelMs, elapsed, prev) != prev) { }
    }

    /// <summary>
    /// Returns the longest QUIC stream teardown since the last call, and starts the next period at 0, whether or not
    /// <see cref="Enabled"/>.
    /// </summary>
    /// <returns>The longest teardown, in milliseconds; 0 when none was timed.</returns>
    public static long TakeMaxStreamCancelMs() => Interlocked.Exchange(ref _maxStreamCancelMs, 0);



    // ---- in-flight sends -------------------------------------------------------------------------
    /// <summary>
    /// Adds a send to the in-flight QUIC send-bytes counter, while <see cref="Enabled"/>, and returns what it counted:
    /// the bytes, or 0. The send's end gives that back to <see cref="SubtractOutstandingSend"/>.
    /// </summary>
    internal static long AddOutstandingSend(long bytes)
    {
        if (!Enabled) return 0;
        Interlocked.Add(ref _outstandingSendBytes, bytes);
        return bytes;
    }

    /// <summary>
    /// Subtracts from the in-flight QUIC send-bytes counter what a send's <see cref="AddOutstandingSend"/> counted,
    /// whatever the log level is by now.
    /// </summary>
    internal static void SubtractOutstandingSend(long countedBytes)
    {
        if (countedBytes != 0)
            Interlocked.Add(ref _outstandingSendBytes, -countedBytes);
    }
}
