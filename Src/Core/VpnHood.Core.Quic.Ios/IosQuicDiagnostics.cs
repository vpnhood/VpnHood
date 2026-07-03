using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Quic.Ios;

/// <summary>
/// iOS QUIC <b>investigation instrumentation</b>, owned by this project (mirrors <c>TcpStackDiagnostics</c>):
/// it holds the QUIC stream counters and the <c>[VHQUIC]</c> open/close logging, and every mutating method
/// is a no-op when <see cref="Enabled"/> is false, so call sites stay clean and it costs nothing in
/// production.
/// </summary>
/// <remarks>
/// Only the diagnostic counters live here. The load-bearing <c>IosQuicClient.FootprintMb</c> jetsam brake
/// input is deliberately NOT gated and stays on <c>IosQuicClient</c>. The host's memory probe reads the
/// public snapshot properties below; it does not own these counters.
/// <para>Off in production; seeded from the <c>VH_IOS_DIAGNOSTICS</c> environment variable (any of
/// <c>1</c>/<c>true</c>/<c>yes</c>) so one switch enables all the iOS diagnostics together.</para>
/// </remarks>
public static class IosQuicDiagnostics
{
    // ---- backing fields --------------------------------------------------------------------------
    private static int _streamSeq;
    private static int _liveStreamCount;
    private static long _outstandingSendBytes;
    private static long _maxStreamCancelMs;

    // ---- public state ----------------------------------------------------------------------------
    /// <summary>Master switch. Defaults to <c>false</c> (production); seeded from <c>VH_IOS_DIAGNOSTICS</c>.</summary>
    public static bool Enabled { get; set; } = ReadEnvDefault();

    /// <summary>Live count of open QUIC streams (= native NWConnections).</summary>
    public static int LiveStreamCount => Volatile.Read(ref _liveStreamCount);
    /// <summary>Bytes handed to <c>nw_connection_send</c> whose completion callback has not yet fired.</summary>
    public static long OutstandingSendBytes => Interlocked.Read(ref _outstandingSendBytes);

    // ---- stream lifecycle ------------------------------------------------------------------------
    /// <summary>
    /// Records a QUIC stream open (logs <c>[VHQUIC] +open</c>) and returns the assigned monotonic id so
    /// the matching close can be paired in the log. Returns 0 (and logs nothing) when disabled.
    /// </summary>
    public static int OnStreamOpened()
    {
        if (!Enabled) return 0;
        var id = Interlocked.Increment(ref _streamSeq);
        var live = Interlocked.Increment(ref _liveStreamCount);
        VhLogger.Instance.LogDebug("[VHQUIC] +open id={Id} live={Live}", id, live);
        return id;
    }

    /// <summary>Records a QUIC stream close (logs <c>[VHQUIC] -close</c>). No-op unless enabled.</summary>
    public static void OnStreamClosed(int id)
    {
        if (!Enabled) return;
        var live = Interlocked.Decrement(ref _liveStreamCount);
        VhLogger.Instance.LogDebug("[VHQUIC] -close id={Id} live={Live}", id, live);
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

    // Seed Enabled from the VH_IOS_DIAGNOSTICS env var (any of 1/true/yes) so one switch turns on all
    // the iOS diagnostics for a dev/simulator run without a code change.
    private static bool ReadEnvDefault()
    {
        try {
            var value = Environment.GetEnvironmentVariable("VH_IOS_DIAGNOSTICS");
            return value is "1" or "true" or "True" or "TRUE" or "yes" or "YES";
        }
        catch {
            return false;
        }
    }
}
