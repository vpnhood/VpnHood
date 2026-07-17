using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Utils;

// ReSharper disable OutParameterValueIsAlwaysDiscarded.Global

namespace VpnHood.Core.Toolkit.Collections;

/// <summary>
/// A concurrent dictionary whose values expire after <see cref="Timeout"/> of no use and are disposed
/// by the dictionary when evicted, replaced or drained. Ordinary operations take no dictionary-wide
/// application lock (the gated cleanup sweep is the only locked path), so it is safe for hot packet paths.
/// Ownership contract:
/// a value handed to the dictionary (or created by a factory) is owned and disposed by the dictionary,
/// except TryAdd returning false, which leaves the caller's value untouched. A factory may run more than
/// once under contention and must return a fresh instance each time; losing instances are disposed.
/// A returned value may be disposed by a concurrent eviction right after retrieval, so callers must
/// tolerate IsDisposed values and value Dispose must be idempotent. Exceptions thrown by a value
/// Dispose are caught and logged, so a throwing value can not strand entries mid-drain.
/// </summary>
public sealed class TimeoutDictionary<TKey, TValue>(TimeSpan? timeout = null) : IDisposable
    where TValue : class, ITimeoutItem where TKey : notnull
{
    // values are wrapped so conditional updates and removals compare wrapper references instead of
    // TValue equality; eviction must target the exact observed entry even if TValue overrides Equals
    private readonly ConcurrentDictionary<TKey, Entry> _items = new();
    private readonly Lock _cleanupLock = new();
    private long _lastCleanupTickCount = long.MinValue / 2; // far past, so the first unforced sweep runs
    private int _disposed;

    public bool AutoCleanup { get; init; } = true;
    public TimeSpan? Timeout { get; init; } = timeout;

    public int Count {
        get {
            AutoCleanupInternal();
            return _items.Count;
        }
    }

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
                created.LastUsedTime = FastDateTime.Now;
                var newEntry = new Entry(created);
                if (_items.TryUpdate(key, newEntry, entry)) {
                    // guard the same-instance case; the factory may have returned the mapped value
                    if (!ReferenceEquals(entry.Value, created))
                        entry.Value.SafeDispose();
                    return FinishPublish(key, newEntry);
                }

                // lost the replacement race; drop the orphan and re-observe the current state
                if (!ReferenceEquals(entry.Value, created))
                    created.SafeDispose();
            }
            else {
                var created = valueFactory(key);
                created.LastUsedTime = FastDateTime.Now;
                var newEntry = new Entry(created);
                if (_items.TryAdd(key, newEntry))
                    return FinishPublish(key, newEntry);

                // lost the insert race; drop the orphan and re-observe the current state
                created.SafeDispose();
            }
        }
    }

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

    public TValue AddOrUpdate(TKey key, TValue value)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        AutoCleanupInternal();

        value.LastUsedTime = FastDateTime.Now;
        var newEntry = new Entry(value);
        while (true) {
            if (_items.TryAdd(key, newEntry))
                return FinishPublish(key, newEntry);

            if (_items.TryGetValue(key, out var entry) && _items.TryUpdate(key, newEntry, entry)) {
                // dispose the displaced value only after the swap succeeded, and never when the
                // caller passed the instance that is already mapped
                if (!ReferenceEquals(entry.Value, value))
                    entry.Value.SafeDispose();
                return FinishPublish(key, newEntry);
            }
        }
    }

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
                value.LastUsedTime = FastDateTime.Now;
                if (_items.TryUpdate(key, newEntry, entry)) {
                    if (!ReferenceEquals(entry.Value, value))
                        entry.Value.SafeDispose();
                    FinishPublish(key, newEntry);
                    return true;
                }
            }
            else {
                value.LastUsedTime = FastDateTime.Now;
                if (_items.TryAdd(key, newEntry)) {
                    FinishPublish(key, newEntry);
                    return true;
                }
            }
        }
    }

    public bool TryRemove(TKey key, out TValue? value)
    {
        if (!_items.TryRemove(key, out var entry)) {
            value = null;
            return false;
        }

        value = entry.Value;
        value.SafeDispose();
        return true;
    }

    private bool IsExpired(TValue item)
    {
        return item.IsDisposed || (Timeout != null && FastDateTime.Now - item.LastUsedTime > Timeout);
    }

    private static void Touch(TValue item)
    {
        // FastDateTime is low-resolution, so most touches see an unchanged time; skipping the write
        // keeps hot entries from dirtying a shared cache line on every packet
        var now = FastDateTime.Now;
        if (item.LastUsedTime != now)
            item.LastUsedTime = now;
    }

    private bool TryRemoveEntry(TKey key, Entry entry)
    {
        // dictionary-initiated disposal uses SafeDispose and must never throw: an exception would
        // abort a drain half-way, skip the publishing handshake or propagate into packet threads
        if (!_items.TryRemove(new KeyValuePair<TKey, Entry>(key, entry)))
            return false;

        entry.Value.SafeDispose();
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        RemoveAll();
    }

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
