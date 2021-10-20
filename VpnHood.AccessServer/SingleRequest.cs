using System;
using System.Collections.Generic;
using System.Threading;

namespace VpnHood.AccessServer
{
    class SingleRequest : IDisposable
    {
        private static readonly Dictionary<string, DateTime> _collection = new();
        private readonly string _name;

        public SingleRequest(string name)
            : this(name, TimeSpan.FromMinutes(5))
        {
        }

        public SingleRequest(string name, TimeSpan expireIn)
        {
            _name = name;
            lock (_collection)
            {
                if (_collection.TryGetValue(name, out var expiration) && DateTime.Now < expiration)
                    throw new InvalidOperationException("Same request of you is in progress!");
                _collection.TryAdd(name, DateTime.Now.Add(expireIn));
            }
        }

        public void Dispose()
        {
            lock (_collection)
                _collection.Remove(_name);
        }
    }
}