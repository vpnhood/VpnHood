namespace VpnHood.Net.Toolkit.Collections;

/// <summary>
/// Provides a base for the values of a <see cref="TimeoutDictionary{TKey,TValue}"/>: the last-used time and the
/// disposed state.
/// </summary>
public class TimeoutItem : ITimeoutItem
{
    private int _disposeStarted;

    /// <inheritdoc/>
    public DateTime LastUsedTime { get; set; }

    /// <inheritdoc/>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Releases the item. A derived class releases its own resources here and calls the base, which marks the item
    /// disposed.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        _ = disposing;
        IsDisposed = true;
    }

    /// <summary>Disposes the item, once: a second call, or one at the same moment, does nothing.</summary>
    public void Dispose()
    {
        // one caller runs the cleanup: two at the same moment would both find the item not disposed yet, and a
        // derived class would release what it holds twice
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        // Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}