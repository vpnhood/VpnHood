using System;

namespace VpnHood.AccessServer.Caching;

public class SessionCache
{
    public AccessCache Access { get; }
    public DateTime ModifiedTime { get; } = DateTime.UtcNow;

    public SessionCache(AccessCache access)
    {
        Access = access;
    }
}