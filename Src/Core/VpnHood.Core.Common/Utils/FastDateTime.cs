namespace VpnHood.Core.Common.Utils;

public static class FastDateTime
{
    private static readonly object Locker = new();
    private static DateTime _lastTime = DateTime.Now;
    private static int _lastTickCount = Environment.TickCount;
    private static DateTime _lastTimeUtc = DateTime.UtcNow;
    private static int _lastTickCountUtc = Environment.TickCount;

    public static TimeSpan Precision { get; set; } = TimeSpan.FromSeconds(1);

    public static DateTime Now {
        get {
            lock (Locker) {
                var tickCount = Environment.TickCount;
                if (tickCount - _lastTickCount >= Precision.Milliseconds || tickCount < _lastTickCount) {
                    _lastTime = DateTime.Now;
                    _lastTickCount = tickCount;
                }

                return _lastTime;
            }
        }
    }

    public static DateTime UtcNow {
        get {
            lock (Locker) {
                var tickCount = Environment.TickCount;
                if (tickCount - _lastTickCountUtc >= Precision.Milliseconds || tickCount < _lastTickCountUtc) {
                    _lastTimeUtc = DateTime.UtcNow;
                    _lastTickCountUtc = tickCount;
                }

                return _lastTimeUtc;
            }
        }
    }
}