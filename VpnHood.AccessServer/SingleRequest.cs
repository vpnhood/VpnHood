using System;
using System.Collections.Generic;

namespace VpnHood.AccessServer;

internal class SingleRequest : IDisposable //todo replace with asyncLock
{
    private static readonly Dictionary<string, DateTime> Collection = new();
    private readonly string _name;

    private SingleRequest(string name)
    {
        _name = name;
    }

    public static IDisposable Start(string name) => Start(name, TimeSpan.FromMinutes(5));

    public static IDisposable Start(string name, TimeSpan expireIn)
    {
        lock (Collection)
        {
            if (Collection.TryGetValue(name, out var expiration))
            {
                if (DateTime.Now < expiration)
                    throw new InvalidOperationException("Same request is in progress!");
                Collection.Remove(name);
            }
            Collection.TryAdd(name, DateTime.Now.Add(expireIn));
            return new SingleRequest(name);
        }
    }

    public void Dispose()
    {
        lock (Collection)
        {
            if (Collection.ContainsKey(_name))
                Collection.Remove(_name);
        }
    }
}