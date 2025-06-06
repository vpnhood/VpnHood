﻿namespace VpnHood.Core.Toolkit.Collections;

public class TimeoutItem : ITimeoutItem
{
    public DateTime LastUsedTime { get; set; }
    public bool IsDisposed { get; private set; }

    protected virtual void Dispose(bool disposing)
    {
        IsDisposed = true;
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}