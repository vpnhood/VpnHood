using System;

namespace VpnHood.Common.Timing;

public static class FastDateTime
{
    private static DateTime _lastTime = DateTime.Now;
    private static int _lastTickCount = Environment.TickCount;
    public static TimeSpan Precision { get; set; } = TimeSpan.FromSeconds(1);

    public static DateTime Now
    {
        get
        {
            if (Environment.TickCount - _lastTickCount >= Precision.Milliseconds || 
                Environment.TickCount < _lastTickCount)
            {
                _lastTime = DateTime.Now;
                _lastTickCount = Environment.TickCount;
            }
            return _lastTime;
        }
    }
}