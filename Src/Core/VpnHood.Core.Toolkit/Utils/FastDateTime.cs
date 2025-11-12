namespace VpnHood.Core.Toolkit.Utils;

public static class FastDateTime
{
    private static readonly object Locker = new();
    private static int _lastTickCount = Environment.TickCount;
    private static int _lastTickCountUtc = Environment.TickCount;

    public static TimeSpan Precision { get; set; } = TimeSpan.FromSeconds(1);

    public static DateTime Now {
        get {
            lock (Locker) {
                var tickCount = Environment.TickCount;
                if (tickCount - _lastTickCount >= Precision.Milliseconds || tickCount < _lastTickCount) {
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
                var tickCount = Environment.TickCount;
                if (tickCount - _lastTickCountUtc >= Precision.Milliseconds || tickCount < _lastTickCountUtc) {
                    field = DateTime.UtcNow;
                    _lastTickCountUtc = tickCount;
                }

                return field;
            }
        }
    } = DateTime.UtcNow;
}