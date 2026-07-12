using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Memory;

namespace VpnHood.Core.Quic.Ios;

/// <summary>
/// iOS QUIC <b>investigation instrumentation</b>, owned by this project (mirrors <c>TcpStackDiagnostics</c>):
/// it holds the QUIC stream counters and the <c>[VHQUIC]</c> open/close logging; every mutating method
/// is a no-op when <see cref="Enabled"/> is false, so call sites stay clean, and it costs nothing in
/// production.
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
    private static long _outstandingSendBytes;
    private static long _maxStreamCancelMs;

    // ---- public state ----------------------------------------------------------------------------
    /// <summary>Read-only master gate: on whenever the effective log level is below Information.</summary>
    public static bool Enabled => VhLogger.MinLogLevel < LogLevel.Information;

    /// <summary>Live count of open QUIC streams (= native NWConnections).</summary>
    public static int LiveStreamCount => Volatile.Read(ref _liveStreamCount);
    /// <summary>Bytes handed to <c>nw_connection_send</c> whose completion callback has not yet fired.</summary>
    public static long OutstandingSendBytes => Interlocked.Read(ref _outstandingSendBytes);

    // ---- stream lifecycle ------------------------------------------------------------------------
    /// <summary>
    /// Records a QUIC stream open (logs <c>[VHQUIC] +CONN</c>) and returns the assigned monotonic id so
    /// the matching close can be paired in the log. Returns 0 (and logs nothing) when disabled.
    /// </summary>
    public static int OnStreamOpened()
    {
        if (!Enabled) return 0;
        var id = Interlocked.Increment(ref _streamSeq);
        var live = Interlocked.Increment(ref _liveStreamCount);
        LogConn("+CONN", live, id);
        return id;
    }

    /// <summary>Records a QUIC stream close (logs <c>[VHQUIC] -CONN</c>). No-op unless enabled.</summary>
    public static void OnStreamClosed(int id)
    {
        if (!Enabled) return;
        var live = Interlocked.Decrement(ref _liveStreamCount);
        LogConn("-CONN", live, id);
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
    public static long BeginTiming() => Enabled ? Environment.TickCount64 : 0;

    /// <summary>Records a completed QUIC stream teardown, tracking the worst duration.</summary>
    public static void EndStreamTeardown(long startTimestamp)
    {
        if (!Enabled) return;
        var elapsed = Environment.TickCount64 - startTimestamp;
        long prev;
        while (elapsed > (prev = Volatile.Read(ref _maxStreamCancelMs)) &&
               Interlocked.CompareExchange(ref _maxStreamCancelMs, elapsed, prev) != prev) { }
    }

    /// <summary>Worst single QUIC stream teardown (ms) since the last call; reading resets it to 0.</summary>
    public static long TakeMaxStreamCancelMs() => Interlocked.Exchange(ref _maxStreamCancelMs, 0);



    // ---- in-flight sends -------------------------------------------------------------------------
    /// <summary>Adds to the in-flight QUIC send-bytes counter. No-op unless <see cref="Enabled"/>.</summary>
    public static void AddOutstandingSend(long bytes)
    {
        if (Enabled)
            Interlocked.Add(ref _outstandingSendBytes, bytes);
    }

    /// <summary>Subtracts from the in-flight QUIC send-bytes counter. No-op unless <see cref="Enabled"/>.</summary>
    public static void SubtractOutstandingSend(long bytes)
    {
        if (Enabled)
            Interlocked.Add(ref _outstandingSendBytes, -bytes);
    }
}
