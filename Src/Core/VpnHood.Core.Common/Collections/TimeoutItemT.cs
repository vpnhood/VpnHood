namespace VpnHood.Core.Common.Collections;

public sealed class TimeoutItem<T>(T value, bool autoDispose = false) : TimeoutItem
{
    public T Value { get; set; } = value;

    protected override void Dispose(bool disposing)
    {
        if (Disposed)
            return;

        if (autoDispose && Value is IDisposable disposable)
            disposable.Dispose();

        base.Dispose(disposing);
    }
}