using System;

namespace VpnHood.Common.Collections;

public class TimeoutItem : ITimeoutItem
{
    public DateTime LastUsedTime { get; set; }
    public bool Disposed { get; private set; }

    protected virtual void Dispose(bool disposing)
    {
        Disposed = true;
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}