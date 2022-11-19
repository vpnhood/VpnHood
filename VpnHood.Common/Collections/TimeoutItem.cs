using System;

namespace VpnHood.Common.Collections;

public class TimeoutItem<T> : ITimeoutItem
{
    private readonly bool _autoDispose;
    public DateTime AccessedTime { get; set ; }

    public bool IsDisposed { get; private set; }
    public T Value { get; set; }

    public TimeoutItem(T value, bool autoDispose)
    {
        _autoDispose = autoDispose;
        Value = value;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_autoDispose && Value is IDisposable disposable)
            disposable.Dispose();
        IsDisposed = true;
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}