using System;

namespace VpnHood.Common.Collections;

public class DisposableTimeoutItem<T> : TimeoutItem<T> where T : IDisposable
{
    public DisposableTimeoutItem(T value) : base(value)
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Value.Dispose();
        }
        base.Dispose(disposing);
    }
}