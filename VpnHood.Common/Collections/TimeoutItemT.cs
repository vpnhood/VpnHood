using System;

namespace VpnHood.Common.Collections;

public class TimeoutItem<T> : TimeoutItem
{
    private readonly bool _autoDispose;

    public T Value { get; set; }

    public TimeoutItem(T value, bool autoDispose)
    {
        _autoDispose = autoDispose;
        Value = value;
    }

    protected override void Dispose(bool disposing)
    {
        if (IsDisposed)
            return;

        if (_autoDispose && Value is IDisposable disposable)
            disposable.Dispose();

        base.Dispose(disposing);
    }
}