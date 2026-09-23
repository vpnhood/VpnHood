namespace VpnHood.Core.Toolkit.Collections;

/// <summary>
/// An item managed by <see cref="TimeoutDictionary{TKey,TValue}"/>. Members are read and written
/// concurrently without synchronization, and the dictionary may dispose an item that another thread
/// still references, so Dispose must be idempotent and the item must tolerate use after disposal.
/// Exceptions thrown by Dispose are caught and logged by the dictionary, never rethrown.
/// </summary>
public interface ITimeoutItem : IDisposable
{
    /// <summary>The last time the item was used. Refreshed by the dictionary on successful access.</summary>
    DateTime LastUsedTime { get; set; }

    /// <summary>A disposed item is treated as expired and is evicted on access or by cleanup.</summary>
    bool IsDisposed { get; }
}
