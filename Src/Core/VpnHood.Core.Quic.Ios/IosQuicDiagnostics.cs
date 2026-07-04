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

    // Fixed-width (5 chars, F1: "  8.3", " 43.2", "102.4") footprint column so the mem field stays aligned
    // across +open/-close lines when eyeballing the live device console — exactly like TcpStack's +CONN/-CONN.
    // The number is the live phys_footprint the host memory limit is enforced against (iOS jetsam),
    // read from the device-set VhMemory.Instance; null on platforms that don't supply it → "  n/a".
    private static string FootprintFixed()
    {
        var mb = VhMemory.Instance.GetInfo().ProcessFootprintMb;
        return mb.HasValue ? $"{mb.Value,5:F1}" : "  n/a";
    }

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
        // FORMAT mirrors TcpStack's +CONN: fixed-width live/mem columns FIRST so they stay in the same
        // on-screen columns in the live device console; the stream id trails (QUIC's analogue of the pair).
        VhLogger.Instance.LogDebug(IosQuicEventIds.Quic,
            "[VHQUIC] +open live={Live} mem={Memory}MB id={Id}",
            live.ToString("D3"), FootprintFixed(), id);
        return id;
    }

    /// <summary>Records a QUIC stream close (logs <c>[VHQUIC] -close</c>). No-op unless enabled.</summary>
    public static void OnStreamClosed(int id)
    {
        if (!Enabled) return;
        var live = Interlocked.Decrement(ref _liveStreamCount);
        // FORMAT: same fixed-width live/mem columns as +open (and TcpStack's -CONN); id trails at the end.
        VhLogger.Instance.LogDebug(IosQuicEventIds.Quic,
            "[VHQUIC] -close live={Live} mem={Memory}MB id={Id}",
            live.ToString("D3"), FootprintFixed(), id);
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

    // ---- jetsam brake tracing --------------------------------------------------------------------
    private static long _brakeCount;
    private static long _hardBrakeCount;
    private static double _brakePeakFootprintMb;
    private static long _lastBrakeLogTick;

    /// <summary>
    /// Records a jetsam-brake activation (from <c>IosQuicStream.ThrottledReadAsync</c>). Every call is
    /// counted, but a summary line is emitted at most once per second — so a brake that fires on every
    /// read while the footprint sits near the limit produces ~1 line/sec instead of flooding the log.
    /// The line reports how many brakes (and how many HARD brakes) fired in the window and the peak
    /// footprint seen, which is what you actually want to know. No-op unless <see cref="Enabled"/>.
    /// </summary>
    public static void TraceBrake(double footprintMb, bool hard)
    {
        if (!Enabled) return;

        Interlocked.Increment(ref _brakeCount);
        if (hard) Interlocked.Increment(ref _hardBrakeCount);

        // Track the worst footprint seen this window (best-effort CAS loop). The success check
        // compares bit patterns because CompareExchange itself compares doubles bitwise.
        while (true) {
            var prevPeak = Volatile.Read(ref _brakePeakFootprintMb);
            if (footprintMb <= prevPeak)
                break;
            var witnessed = Interlocked.CompareExchange(ref _brakePeakFootprintMb, footprintMb, prevPeak);
            if (BitConverter.DoubleToInt64Bits(witnessed) == BitConverter.DoubleToInt64Bits(prevPeak))
                break;
        }

        // Time-throttle: only one thread per ~1s window wins the right to flush + log.
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastBrakeLogTick);
        if (now - last < 1000) return;
        if (Interlocked.CompareExchange(ref _lastBrakeLogTick, now, last) != last) return;

        var count = Interlocked.Exchange(ref _brakeCount, 0);
        var hardCount = Interlocked.Exchange(ref _hardBrakeCount, 0);
        var peak = Interlocked.Exchange(ref _brakePeakFootprintMb, 0);
        var windowMs = last == 0 ? 0 : now - last;
        VhLogger.Instance.LogDebug(IosQuicEventIds.Quic,
            "[VHQUIC] brake x{Count} (hard={Hard}) peak={Peak}MB over {WindowMs}ms",
            count, hardCount, peak.ToString("F1"), windowMs);
    }

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
