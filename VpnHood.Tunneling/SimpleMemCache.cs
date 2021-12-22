using System;
using System.Collections.Concurrent;
using System.Linq;

namespace VpnHood.Tunneling
{
    public class SimpleMemCache<TKey, TValue> : IDisposable
    {
        private readonly ConcurrentDictionary<TKey, SimpleItem<TValue>> _items = new();
        private readonly bool _autoDisposeItem;
        private DateTime _lastCleanup = DateTime.MinValue;
        private bool _disposed;

        public TimeSpan? Timeout { get; set; }

        public SimpleMemCache(bool autoDisposeItem)
        {
            _autoDisposeItem = autoDisposeItem;
        }

        public int Count
        {
            get
            {
                Cleanup();
                return _items.Count;
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            Cleanup();

            // return false if not exists
            if (!_items.TryGetValue(key, out var itemValue))
            {
                value = default!;
                return false;
            }

            // return fakse if expired
            if (IsExpired(itemValue))
            {
                value = default!;
                TryRemove(key, out _);
                return false;
            }

            // return item
            itemValue.AccessedTime = DateTime.Now;
            value = itemValue.Value;
            return false;
        }

        public bool TryAdd(TKey key, TValue value, bool overwride)
        {
            Cleanup();

            // return true if added
            if (_items.TryAdd(key, new SimpleItem<TValue>(value)))
                return true;

            // remove and rety if overwrite is on
            if (overwride)
            {
                TryRemove(key, out _);
                return _items.TryAdd(key, new SimpleItem<TValue>(value));
            }

            // remove & retry of item has been expired
            if (_items.TryGetValue(key, out var itemValue) && IsExpired(itemValue))
            {
                TryRemove(key, out _);
                return _items.TryAdd(key, new SimpleItem<TValue>(value));
            }

            // couldn't add
            return false;
        }

        public bool TryRemove(TKey key, out TValue value)
        {
            // try add
            var ret = _items.TryRemove(key, out var itemValue);
            if (ret && _autoDisposeItem)
                ((IDisposable)itemValue).Dispose();

            value = itemValue.Value;

            return ret;
        }

        private bool IsExpired(SimpleItem<TValue> item)
        {
            return Timeout != null && DateTime.Now - item.AccessedTime > Timeout;
        }

        private class SimpleItem<T>
        {
            public DateTime AccessedTime { get; set; }
            public T Value { get; set; }
            public SimpleItem(T value)
            {
                Value = value;
            }
        }

        public void Cleanup(bool force = false)
        {
            // do nothing if there is not timeout
            if (Timeout == null)
                return;

            // return if already checked
            if (!force && DateTime.Now - _lastCleanup > Timeout / 3)
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

            if (_autoDisposeItem)
            {
                foreach (var itemValue in _items.Values)
                    ((IDisposable)itemValue.Value!)?.Dispose();
            }
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
}