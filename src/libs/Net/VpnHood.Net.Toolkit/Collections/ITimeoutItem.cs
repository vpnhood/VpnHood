namespace VpnHood.Net.Toolkit.Collections;

/// <summary>
/// An item managed by <see cref="TimeoutDictionary{TKey,TValue}"/>. Members are read and written
/// concurrently without synchronization, and the dictionary may dispose an item that another thread
/// still references, so Dispose must be idempotent and the item must tolerate use after disposal.
/// Dispose must not throw: the dictionary does not catch it, and a throw would end a drain part-way.
/// </summary>
public interface ITimeoutItem : IDisposable
{
    /// <summary>Gets or sets the last time the item was used, in UTC. The dictionary refreshes it on each access.</summary>
    DateTime LastUsedTime { get; set; }

    /// <summary>
    /// Gets a value indicating whether the item is disposed. A disposed item counts as expired: it is evicted on access,
    /// or by a cleanup when the dictionary has a timeout.
    /// </summary>
    bool IsDisposed { get; }
}
