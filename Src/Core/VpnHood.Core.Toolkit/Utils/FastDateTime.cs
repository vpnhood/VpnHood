namespace VpnHood.Core.Toolkit.Utils;

// todo: SimplyUse Tick64
public static class FastDateTime
{
    // On iOS (especially inside a NetworkExtension/PacketTunnelProvider sandbox),
    // accessing TimeZoneInfo.Local — which DateTime.Now relies on — can hang indefinitely.
    // To stay safe, we use UtcNow as the source on iOS.
    // ToDo: check AOT
    private static readonly bool UseUtcSource = OperatingSystem.IsIOS();

    private static readonly Lock Locker = new();
    private static int _lastTickCount = Environment.TickCount;
    private static int _lastTickCountUtc = Environment.TickCount;

    public static TimeSpan Precision { get; set; } = TimeSpan.FromSeconds(1);

    public static DateTime Now {
        get {
            lock (Locker) {
                var tickCount = Environment.TickCount;
                if (tickCount - _lastTickCount >= Precision.TotalMilliseconds || tickCount < _lastTickCount) {
                    field = UseUtcSource ? DateTime.UtcNow : DateTime.Now;
                    _lastTickCount = tickCount;
                }

                return field;
            }
        }
    } = UseUtcSource ? DateTime.UtcNow : DateTime.Now;

    public static DateTime UtcNow {
        get {
            lock (Locker) {
                var tickCount = Environment.TickCount;
                if (tickCount - _lastTickCountUtc >= Precision.TotalMilliseconds || tickCount < _lastTickCountUtc) {
                    field = DateTime.UtcNow;
                    _lastTickCountUtc = tickCount;
                }

                return field;
            }
        }
    } = DateTime.UtcNow;
}