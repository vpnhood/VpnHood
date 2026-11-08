using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using VpnHood.Net.Toolkit.Utils;

// ReSharper disable OutParameterValueIsAlwaysDiscarded.Global

namespace VpnHood.Net.Toolkit.Collections;

/// <summary>
/// A concurrent dictionary whose values expire after <see cref="Timeout"/> of no use and are disposed
/// by the dictionary when evicted, replaced or drained. Ordinary operations take no dictionary-wide
/// application lock (the gated cleanup sweep is the only locked path). Expiry is checked on access and by the sweeps
/// the operations try, with no timer of its own, against a cached clock of about one second's precision.
/// Ownership contract:
/// a value the dictionary adds (or a factory creates) is owned and disposed by the dictionary. One TryAdd returns false
/// for stays the caller's, though its last-used time may be set; one AddOrUpdate rejects because the dictionary is
/// already disposed stays the caller's. A factory may run more than
/// once under contention and must return a fresh instance each time; losing instances are disposed.
/// A returned value may be disposed by a concurrent eviction right after retrieval, so callers must
/// tolerate IsDisposed values and value Dispose must be idempotent. A value's Dispose must not throw: the
/// dictionary does not catch it, and a throw would end a drain part-way.
/// </summary>
/// <typeparam name="TKey">The type of the keys.</typeparam>
/// <typeparam name="TValue">The type of the values.</typeparam>
/// <param name="timeout">
/// How long a value may go unused before it expires, or <see langword="null"/> for no expiry by disuse; a disposed value
/// still counts as expired on access.
/// </param>
public sealed class TimeoutDictionary<TKey, TValue>(TimeSpan? timeout = null) : IDisposable
    where TValue : class, ITimeoutItem where TKey : notnull
{
    // values are wrapped so conditional updates and removals compare wrapper references instead of
    // TValue equality; eviction must target the exact observed entry even if TValue overrides Equals
    private readonly ConcurrentDictionary<TKey, Entry> _items = new();
    private readonly Lock _cleanupLock = new();
    private long _lastCleanupTickCount = long.MinValue / 2; // far past, so the first unforced sweep runs
    private int _disposed;

    /// <summary>
    /// Gets a value indicating whether an add or a lookup first tries a sweep of the expired values, which runs at most
    /// once per a third of <see cref="Timeout"/>; removals and disposal do not. The default is <see langword="true"/>.
    /// </summary>
    public bool AutoCleanup { get; init; } = true;

    /// <summary>
    /// Gets how long a value may go unused before it expires, or <see langword="null"/> for no expiry by disuse; a
    /// disposed value still counts as expired on access.
    /// </summary>
    public TimeSpan? Timeout { get; init; } = timeout;

    /// <summary>
    /// Gets the number of values, expired ones not swept out yet included; with <see cref="AutoCleanup"/>, it tries a
    /// sweep first.
    /// </summary>
    public int Count {
        get {
            AutoCleanupInternal();
            return _items.Count;
        }
    }

    /// <summary>
    /// Gets a key's live value, refreshing its last-used time, or adds the value a factory creates, in place of an
    /// expired one, which is disposed.
    /// </summary>
    /// <remarks>
    /// Under contention the factory may run more than once; it must return a new instance each time, and the instances
    /// that lose are disposed.
    /// </remarks>
    /// <param name="key">The key.</param>
    /// <param name="valueFactory">Creates the value to add.</param>
    /// <returns>The key's value, which the dictionary owns.</returns>
    /// <exception cref="ObjectDisposedException">The dictionary is disposed.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        AutoCleanupInternal();

        while (true) {
            if (_items.TryGetValue(key, out var entry)) {
                if (!IsExpired(entry.Value)) {
                    Touch(entry.Value);
                    return entry.Value;
                }

                // replace the expired entry. The new value is stamped before publication so a
                // concurrent reader can not see it as expired and evict it
                var created = valueFactory(key);
                created.LastUsedTime = FastDateTime.UtcNow;
                var newEntry = new Entry(created);
                if (_items.TryUpdate(key, newEntry, entry)) {
                    // guard the same-instance case; the factory may have returned the mapped value
                    if (!ReferenceEquals(entry.Value, created))
                        entry.Value.Dispose();
                    return FinishPublish(key, newEntry);
                }

                // lost the replacement race; drop the orphan and re-observe the current state
                if (!ReferenceEquals(entry.Value, created))
                    created.Dispose();
            }
            else {
                var created = valueFactory(key);
                created.LastUsedTime = FastDateTime.UtcNow;
                var newEntry = new Entry(created);
                if (_items.TryAdd(key, newEntry))
                    return FinishPublish(key, newEntry);

                // lost the insert race; drop the orphan and re-observe the current state
                created.Dispose();
            }
        }
    }

    /// <summary>
    /// Gets a key's live value, refreshing its last-used time. An expired value is removed and disposed.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value, when found; it stays the dictionary's.</param>
    /// <returns><see langword="true"/> if the key has a live value; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        AutoCleanupInternal();

        // retry after losing an eviction race: a writer may have replaced the expired entry with a
        // live one, and the caller must observe that replacement rather than a false miss
        while (_items.TryGetValue(key, out var entry)) {
            if (!IsExpired(entry.Value)) {
                Touch(entry.Value);
                value = entry.Value;
                return true;
            }

            // evict only the observed entry, and do not touch or return the expired value; a
            // concurrent replacer may already have disposed it
            if (TryRemoveEntry(key, entry)) {
                value = null;
                return false;
            }
        }

        value = null;
        return false;
    }

    /// <summary>Adds a value, or replaces a key's value and disposes the replaced one.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">
    /// The value, which the dictionary owns once added; when the dictionary is already disposed, it throws and the caller
    /// keeps the value, and a disposal during the add disposes it.
    /// </param>
    /// <returns>The value.</returns>
    /// <exception cref="ObjectDisposedException">The dictionary is disposed.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public TValue AddOrUpdate(TKey key, TValue value)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        AutoCleanupInternal();

        value.LastUsedTime = FastDateTime.UtcNow;
        var newEntry = new Entry(value);
        while (true) {
            if (_items.TryAdd(key, newEntry))
                return FinishPublish(key, newEntry);

            if (_items.TryGetValue(key, out var entry) && _items.TryUpdate(key, newEntry, entry)) {
                // dispose the displaced value only after the swap succeeded, and never when the
                // caller passed the instance that is already mapped
                if (!ReferenceEquals(entry.Value, value))
                    entry.Value.Dispose();
                return FinishPublish(key, newEntry);
            }
        }
    }

    /// <summary>
    /// Adds a value unless the key has a live one; an expired one is replaced and disposed.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value, which the dictionary owns when it is added; its last-used time may be set either way.</param>
    /// <returns>
    /// <see langword="true"/> if the value was added; <see langword="false"/> if the key has a live value or the
    /// dictionary is disposed, and the caller keeps the value.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool TryAdd(TKey key, TValue value)
    {
        // Try* semantics: returning false leaves the caller's value untouched and caller-owned
        if (Volatile.Read(ref _disposed) != 0)
            return false;

        AutoCleanupInternal();

        var newEntry = new Entry(value);
        while (true) {
            if (_items.TryGetValue(key, out var entry)) {
                if (!IsExpired(entry.Value))
                    return false;

                // an expired occupant must not block a fresh add; replace it like GetOrAdd does
                value.LastUsedTime = FastDateTime.UtcNow;
                if (_items.TryUpdate(key, newEntry, entry)) {
                    if (!ReferenceEquals(entry.Value, value))
                        entry.Value.Dispose();
                    FinishPublish(key, newEntry);
                    return true;
                }
            }
            else {
                value.LastUsedTime = FastDateTime.UtcNow;
                if (_items.TryAdd(key, newEntry)) {
                    FinishPublish(key, newEntry);
                    return true;
                }
            }
        }
    }

    /// <summary>Removes a key's value and disposes it.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The removed value, already disposed, when found.</param>
    /// <returns><see langword="true"/> if a value was removed; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool TryRemove(TKey key, out TValue? value)
    {
        if (!_items.TryRemove(key, out var entry)) {
            value = null;
            return false;
        }

        value = entry.Value;
        value.Dispose();
        return true;
    }

    private bool IsExpired(TValue item)
    {
        return item.IsDisposed || (Timeout != null && FastDateTime.UtcNow - item.LastUsedTime > Timeout);
    }

    private static void Touch(TValue item)
    {
        // FastDateTime is low-resolution, so most touches see an unchanged time; skipping the write
        // keeps hot entries from dirtying a shared cache line on every packet
        var now = FastDateTime.UtcNow;
        if (item.LastUsedTime != now)
            item.LastUsedTime = now;
    }

    private bool TryRemoveEntry(TKey key, Entry entry)
    {
        // an item's close, ours, never throws: one that did would abort a drain half-way, skip the publishing
        // handshake or propagate into packet threads
        if (!_items.TryRemove(new KeyValuePair<TKey, Entry>(key, entry)))
            return false;

        entry.Value.Dispose();
        return true;
    }

    private TValue FinishPublish(TKey key, Entry entry)
    {
        // Dispose may have drained the map before this entry landed. The Interlocked gate in Dispose
        // plus this recheck guarantees the entry is still disposed, by exactly one side
        if (Volatile.Read(ref _disposed) != 0)
            TryRemoveEntry(key, entry);

        return entry.Value;
    }

    private void AutoCleanupInternal()
    {
        if (AutoCleanup)
            Cleanup();
    }

    /// <summary>
    /// Removes and disposes the expired values. It does nothing without a <see cref="Timeout"/>, or when the dictionary
    /// is disposed.
    /// </summary>
    /// <param name="force">
    /// <see langword="true"/> to sweep now; <see langword="false"/> to skip the sweep when the last one was within a
    /// third of <see cref="Timeout"/>.
    /// </param>
    public void Cleanup(bool force = false)
    {
        // do nothing if there is no timeout
        var timeout = Timeout;
        if (timeout == null || Volatile.Read(ref _disposed) != 0)
            return;

        // fast path: this runs on hot packet paths, so skip the lock while the last sweep is recent.
        // The gate uses the monotonic TickCount64 so a backward system-clock step can not stall it;
        // item expiration itself stays wall-clock based. A stale read at worst delays one sweep round
        var minMilliseconds = timeout.Value.Ticks / (TimeSpan.TicksPerMillisecond * 3);
        if (!force && Environment.TickCount64 - Volatile.Read(ref _lastCleanupTickCount) < minMilliseconds)
            return;

        // return if already checked
        lock (_cleanupLock) {
            if (!force && Environment.TickCount64 - Volatile.Read(ref _lastCleanupTickCount) < minMilliseconds)
                return;
            Volatile.Write(ref _lastCleanupTickCount, Environment.TickCount64);

            // remove expired items. Eviction is entry-conditional, so a concurrent replacement can
            // not be removed in place of the expired entry it displaced
            foreach (var item in _items) {
                if (IsExpired(item.Value.Value))
                    TryRemoveEntry(item.Key, item.Value);
            }
        }
    }

    /// <summary>
    /// Removes and disposes every value; later adds throw or fail. Calling it again does nothing.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        RemoveAll();
    }

    /// <summary>
    /// Removes and disposes the values, in one pass; a value added or replaced during the pass may stay.
    /// </summary>
    public void RemoveAll()
    {
        // drain with conditional removes; a Values snapshot followed by Clear would drop values
        // added in between without disposing them
        foreach (var item in _items)
            TryRemoveEntry(item.Key, item.Value);
    }

    // reference-identity wrapper: conditional dictionary operations compare this wrapper and never
    // TValue.Equals, so eviction always targets the exact observed entry
    private sealed class Entry(TValue value)
    {
        public readonly TValue Value = value;
    }
}
