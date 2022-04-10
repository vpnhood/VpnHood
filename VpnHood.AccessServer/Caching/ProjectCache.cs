using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Caching;

public class ProjectCache
{
    private readonly Dictionary<Guid, ServerCache> _serverCaches = new();
    private static int _serversSync = 0;
    public static readonly object ServersLock = new();
    public Project Project { get; }
    private Dictionary<long, SessionCache> _sessions  = new();

    public ProjectCache(Project project)
    {
        Project = project;
    }

    public async Task<Models.Server> GetServer(VhContext vhContext, Guid serverId)
    {
        lock (ServersLock)
            if (_serverCaches.TryGetValue(serverId, out var serverCache))
                return serverCache.Server;

        var serversSync = Interlocked.Increment(ref _serversSync);
        var server = await vhContext.Servers.SingleAsync(x => x.ServerId == serverId);

        lock (ServersLock)
        {
            if (serversSync == _serversSync)
                _serverCaches.TryAdd(serverId, new ServerCache(server));
        }

        return server;
    }


}