using System;
using System.Collections.Concurrent;
using System.Linq;

namespace VpnHood.Common.Collections;

public class TimeoutDictionary<TKey, TValue> : IDisposable where TValue : ITimeoutItem
{
    private readonly ConcurrentDictionary<TKey, TValue> _items = new();
    private DateTime _lastCleanup = DateTime.MinValue;
    private bool _disposed;

    public TimeSpan? Timeout { get; set; }

    public TimeoutDictionary(TimeSpan? timeout = null)
    {
        TimeSpan = timeout;
    }

    public int Count
    {
        get
        {
            Cleanup();
            return _items.Count;
        }
    }

    public TimeSpan? TimeSpan { get; }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        lock(_items)
        {
            if (TryGetValue(key, out var value))
                return value;

            value = valueFactory(key);
            if (!TryAdd(key, value, false))
                throw new Exception($"Could not add an item to {GetType().Name}");
            return value;
        }
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        Cleanup();

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
        value.AccessedTime = DateTime.Now;
        return true;
    }

    public bool TryAdd(TKey key, TValue value, bool overwride)
    {
        Cleanup();
        value.AccessedTime = DateTime.Now;

        // return true if added
        if (_items.TryAdd(key, value))
            return true;

        // remove and rety if overwrite is on
        if (overwride)
        {
            TryRemove(key, out _);
            return _items.TryAdd(key, value);
        }

        // remove & retry of an item that has been expired
        if (_items.TryGetValue(key, out var prevValue) && IsExpired(prevValue))
        {
            TryRemove(key, out _);
            return _items.TryAdd(key, value);
        }

        // couldn't add
        return false;
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
        return item.IsDisposed || (Timeout != null && DateTime.Now - item.AccessedTime > Timeout);
    }

    public void Cleanup(bool force = false)
    {
        // do nothing if there is not timeout
        if (Timeout == null)
            return;

        // return if already checked
        if (!force && DateTime.Now - _lastCleanup < Timeout / 3)
            return;
        _lastCleanup = DateTime.Now;

        // remove timeout items
        foreach (var item in _items.Where(x => IsExpired(x.Value)))
            TryRemove(item.Key, out _);
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
            if (oldestAccessedTime < item.Value.AccessedTime)
            {
                oldestAccessedTime = item.Value.AccessedTime;
                oldestKey = item.Key;
            }
        }

        if (oldestKey != null)
            TryRemove(oldestKey, out _);
    }
}