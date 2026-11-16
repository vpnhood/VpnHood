namespace VpnHood.Net.Toolkit.Collections;

/// <summary>
/// Provides a <see cref="TimeoutItem"/> that holds a value.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <param name="value">The value.</param>
/// <param name="autoDispose">
/// <see langword="true"/> to dispose the value with the item, when it is <see cref="IDisposable"/>.
/// </param>
public sealed class TimeoutItem<T>(T value, bool autoDispose = false) : TimeoutItem
{
    /// <summary>Gets or sets the value.</summary>
    public T Value { get; set; } = value;

    /// <inheritdoc/>
    /// <remarks>
    /// It disposes the value it holds too when made with autoDispose and the value is disposable; a value replaced by a
    /// set is not disposed.
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        if (IsDisposed)
            return;

        if (autoDispose && Value is IDisposable disposable)
            disposable.Dispose();

        base.Dispose(disposing);
    }
}