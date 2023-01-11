using System;

namespace VpnHood.Common.Timing;

public static class FastDateTime
{
    private static readonly object _lock = new();
    private static DateTime _lastTime = DateTime.Now;
    private static int _lastTickCount = Environment.TickCount;
    public static TimeSpan Precision { get; set; } = TimeSpan.FromSeconds(1);

    public static DateTime Now
    {
        get
        {
            lock (_lock)
            {
                var tickCount = Environment.TickCount;
                if (tickCount - _lastTickCount >= Precision.Milliseconds ||
                    tickCount < _lastTickCount)
                {
                    _lastTime = DateTime.Now;
                    _lastTickCount = tickCount;
                }
                return _lastTime;
            }
        }
    }
}