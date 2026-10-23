namespace VpnHood.Net.Toolkit.Utils;

public static class FastDateTime
{
    private static readonly Lock RefreshLocker = new();
    private static long _utcNowTicks;
    private static long _lastTickCountUtc;

    public static TimeSpan Precision { get; set; } = TimeSpan.FromSeconds(1);

    // Hot packet paths call this several times per packet across all threads, so the
    // within-Precision fast path takes no lock. The refresh slow path is locked so that
    // sampling and publishing are atomic against other refreshers: a stalled refresher
    // blocks the next one instead of racing it, and the cached clock can never roll
    // backward from a stale sample. It still follows system clock changes in both directions.
    // UTC only: the first read of local time in a process loads its time zone, slow on some phones
    public static DateTime UtcNow {
        get {
            var tickCount = Environment.TickCount64;
            if (Volatile.Read(ref _utcNowTicks) == 0 || tickCount - Volatile.Read(ref _lastTickCountUtc) >= Precision.TotalMilliseconds) {
                lock (RefreshLocker) {
                    tickCount = Environment.TickCount64;
                    if (Volatile.Read(ref _utcNowTicks) == 0 || tickCount - Volatile.Read(ref _lastTickCountUtc) >= Precision.TotalMilliseconds) {
                        Volatile.Write(ref _utcNowTicks, DateTime.UtcNow.Ticks);
                        Volatile.Write(ref _lastTickCountUtc, tickCount);
                    }
                }
            }

            return new DateTime(Volatile.Read(ref _utcNowTicks), DateTimeKind.Utc);
        }
    }
}
