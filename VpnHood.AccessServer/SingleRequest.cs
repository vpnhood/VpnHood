using System;
using System.Collections.Generic;

namespace VpnHood.AccessServer;

internal class SingleRequest : IDisposable
{
    private static readonly Dictionary<string, DateTime> _collection = new();
    private readonly string _name;

    private SingleRequest(string name)
    {
        _name = name;
    }

    public static IDisposable Start(string name) => Start(name, TimeSpan.FromMinutes(5));

    public static IDisposable Start(string name, TimeSpan expireIn)
    {
        lock (_collection)
        {
            if (_collection.TryGetValue(name, out var expiration))
            {
                if (DateTime.Now < expiration)
                    throw new InvalidOperationException("Same request is in progress!");
                _collection.Remove(name);
            }
            _collection.TryAdd(name, DateTime.Now.Add(expireIn));
            return new SingleRequest(name);
        }
    }

    public void Dispose()
    {
        lock (_collection)
        {
            if (_collection.ContainsKey(_name))
                _collection.Remove(_name);
        }
    }
}