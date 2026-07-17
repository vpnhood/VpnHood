namespace VpnHood.Core.Toolkit.Utils;

public static class FastDateTime
{
    private static readonly Lock RefreshLocker = new();
    private static long _nowTicks = DateTime.Now.Ticks;
    private static long _utcNowTicks = DateTime.UtcNow.Ticks;
    private static long _lastTickCount = Environment.TickCount64;
    private static long _lastTickCountUtc = Environment.TickCount64;

    public static TimeSpan Precision { get; set; } = TimeSpan.FromSeconds(1);

    // Hot packet paths call this several times per packet across all threads, so the
    // within-Precision fast path takes no lock. The refresh slow path is locked so that
    // sampling and publishing are atomic against other refreshers: a stalled refresher
    // blocks the next one instead of racing it, and the cached clock can never roll
    // backward from a stale sample. It still follows system clock changes in both directions
    public static DateTime Now {
        get {
            var tickCount = Environment.TickCount64;
            if (tickCount - Volatile.Read(ref _lastTickCount) >= Precision.TotalMilliseconds) {
                lock (RefreshLocker) {
                    tickCount = Environment.TickCount64;
                    if (tickCount - Volatile.Read(ref _lastTickCount) >= Precision.TotalMilliseconds) {
                        Volatile.Write(ref _nowTicks, DateTime.Now.Ticks);
                        Volatile.Write(ref _lastTickCount, tickCount);
                    }
                }
            }

            return new DateTime(Volatile.Read(ref _nowTicks), DateTimeKind.Local);
        }
    }

    public static DateTime UtcNow {
        get {
            var tickCount = Environment.TickCount64;
            if (tickCount - Volatile.Read(ref _lastTickCountUtc) >= Precision.TotalMilliseconds) {
                lock (RefreshLocker) {
                    tickCount = Environment.TickCount64;
                    if (tickCount - Volatile.Read(ref _lastTickCountUtc) >= Precision.TotalMilliseconds) {
                        Volatile.Write(ref _utcNowTicks, DateTime.UtcNow.Ticks);
                        Volatile.Write(ref _lastTickCountUtc, tickCount);
                    }
                }
            }

            return new DateTime(Volatile.Read(ref _utcNowTicks), DateTimeKind.Utc);
        }
    }
}
