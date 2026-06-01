namespace VpnHood.Core.Toolkit.Utils;

public static class FastDateTime
{
    // On iOS (especially inside a NetworkExtension/PacketTunnelProvider sandbox),
    // accessing TimeZoneInfo.Local — which DateTime.Now relies on — can hang indefinitely.
    // To stay safe, we use UtcNow as the source on iOS.
    private static readonly bool UseUtcSource = OperatingSystem.IsIOS() || OperatingSystem.IsTvOS();

    private static readonly object Locker = new();
    private static int _lastTickCount = Environment.TickCount;
    private static int _lastTickCountUtc = Environment.TickCount;
    private static DateTime _utcNow = DateTime.UtcNow;
    private static DateTime _now = UseUtcSource ? DateTime.UtcNow : DateTime.Now;

    public static TimeSpan Precision { get; set; } = TimeSpan.FromSeconds(1);

    public static DateTime Now {
        get {
            lock (Locker) {
                var tickCount = Environment.TickCount;
                if (tickCount - _lastTickCount >= Precision.TotalMilliseconds || tickCount < _lastTickCount) {
                    _now = UseUtcSource ? DateTime.UtcNow : DateTime.Now;
                    _lastTickCount = tickCount;
                }

                return _now;
            }
        }
    }

    public static DateTime UtcNow {
        get {
            lock (Locker) {
                var tickCount = Environment.TickCount;
                if (tickCount - _lastTickCountUtc >= Precision.TotalMilliseconds || tickCount < _lastTickCountUtc) {
                    _utcNow = DateTime.UtcNow;
                    _lastTickCountUtc = tickCount;
                }

                return _utcNow;
            }
        }
    }
}