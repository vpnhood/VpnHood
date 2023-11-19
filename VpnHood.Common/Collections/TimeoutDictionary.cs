using System;
using System.Collections.Concurrent;
using System.Linq;
using VpnHood.Common.Utils;

namespace VpnHood.Common.Collections;

public class TimeoutDictionary<TKey, TValue> : IDisposable where TValue : ITimeoutItem
{
    private readonly ConcurrentDictionary<TKey, TValue> _items = new();
    private DateTime _lastCleanupTime = DateTime.MinValue;
    private bool _disposed;

    public bool AutoCleanup { get; set; } = true;
    public TimeSpan? Timeout { get; set; }

    public TimeoutDictionary(TimeSpan? timeout = null)
    {
        Timeout = timeout;
    }

    public int Count
    {
        get
        {
            AutoCleanupInternal();
            return _items.Count;
        }
    }


    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        AutoCleanupInternal();

        // update old item if expired or return the old item
        lock (_items)
        {
            var res = _items.AddOrUpdate(key, valueFactory,
                (k, oldValue) =>
                {
                    if (!IsExpired(oldValue))
                        return oldValue;

                    oldValue.Dispose();
                    return valueFactory(k);
                });

            res.LastUsedTime = FastDateTime.Now;
            return res;
        }
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        AutoCleanupInternal();

        // return false if not exists
        if (!_items.TryGetValue(key, out value))
            return false;

        // return false if expired
        if (IsExpired(value))
        {
            value = default!;
            TryRemove(key, out _);
            return false;
        }

        // return true
        value.LastUsedTime = FastDateTime.Now;
        return true;
    }

    public TValue AddOrUpdate(TKey key, TValue value)
    {
        AutoCleanupInternal();

        lock (_items)
        {
            var res = _items.AddOrUpdate(key, value, (_, oldValue) =>
            {
                oldValue.Dispose();
                return value;
            });

            res.LastUsedTime = FastDateTime.Now;
            return res;
        }
    }

    public bool TryAdd(TKey key, TValue value)
    {
        AutoCleanupInternal();

        // return true if added
        if (!_items.TryAdd(key, value))
            return false;

        value.LastUsedTime = FastDateTime.Now;
        return true;

    }

    public bool TryRemove(TKey key, out TValue value)
    {
        // try add
        var ret = _items.TryRemove(key, out value);
        if (ret)
            value.Dispose();

        return ret;
    }

    private bool IsExpired(ITimeoutItem item)
    {
        return item.Disposed || (Timeout != null && FastDateTime.Now - item.LastUsedTime > Timeout);
    }

    private void AutoCleanupInternal()
    {
        if (AutoCleanup)
            Cleanup();
    }

    private readonly object _cleanupLock = new();
    public void Cleanup(bool force = false)
    {
        // do nothing if there is not timeout
        if (Timeout == null)
            return;

        // return if already checked
        lock (_cleanupLock)
        {
            if (!force && FastDateTime.Now - _lastCleanupTime < Timeout / 3)
                return;
            _lastCleanupTime = FastDateTime.Now;

            // remove timeout items
            foreach (var item in _items.Where(x => IsExpired(x.Value)))
                TryRemove(item.Key, out _);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var value in _items.Values)
            value.Dispose();

        _items.Clear();
    }

    public void RemoveOldest()
    {
        var oldestAccessedTime = DateTime.MaxValue;
        var oldestKey = default(TKey?);
        foreach (var item in _items)
        {
            if (oldestAccessedTime < item.Value.LastUsedTime)
            {
                oldestAccessedTime = item.Value.LastUsedTime;
                oldestKey = item.Key;
            }
        }

        if (oldestKey != null)
            TryRemove(oldestKey, out _);
    }
}