using System;

namespace VpnHood.Common.Utils;

public static class FastDateTime
{
    private static DateTime _lastTime = DateTime.Now;
    private static int _lastTickCount = Environment.TickCount;
    public static DateTime Now
    {
        get
        {
            if (Environment.TickCount - _lastTickCount > 1000)
            {
                _lastTime = DateTime.Now;
                _lastTickCount = Environment.TickCount;
            }
            return _lastTime;
        }
    }
}