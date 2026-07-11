namespace VpnHood.Core.Toolkit.Utils;

public static class FastDateTime
{
    private static readonly Lock Locker = new();
    private static long _lastTickCount = Environment.TickCount64;
    private static long _lastTickCountUtc = Environment.TickCount64;

    public static TimeSpan Precision { get; set; } = TimeSpan.FromSeconds(1);

    public static DateTime Now {
        get {
            lock (Locker) {
                var tickCount = Environment.TickCount64;
                if (tickCount - _lastTickCount >= Precision.TotalMilliseconds) {
                    field = DateTime.Now;
                    _lastTickCount = tickCount;
                }

                return field;
            }
        }
    } = DateTime.Now;

    public static DateTime UtcNow {
        get {
            lock (Locker) {
                var tickCount = Environment.TickCount64;
                if (tickCount - _lastTickCountUtc >= Precision.TotalMilliseconds) {
                    field = DateTime.UtcNow;
                    _lastTickCountUtc = tickCount;
                }

                return field;
            }
        }
    } = DateTime.UtcNow;
}