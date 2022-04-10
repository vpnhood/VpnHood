using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Caching;

public class SystemCache
{
    private readonly Dictionary<Guid, ProjectCache> _projects = new();
    private readonly Dictionary<Guid, Models.Server?> _servers = new();
    private int _projectsSync;
    private readonly object _projectsLock = new();
    private int _serversSync;
    private readonly object _serversLock = new();

    public async Task<Project> GetProject(VhContext vhContext, Guid projectId)
    {
        lock (_projectsLock)
            if (_projects.TryGetValue(projectId, out var projectCache))
                return projectCache.Project;

        var projectsSync = Interlocked.Increment(ref _projectsSync);
        var project = await vhContext.Projects.SingleAsync(x => x.ProjectId == projectId);

        lock (_projectsLock)
        {
            if (projectsSync == _projectsSync)
                _projects.TryAdd(projectId, new ProjectCache(project));
        }

        return project;
    }

    public async Task<Models.Server> GetServer(VhContext vhContext, Guid serverId)
    {
        Models.Server? server;

        lock (_serversLock)
        {
            if (_servers.TryGetValue(serverId, out server))
                return server ?? throw new KeyNotFoundException();
        }

        var sync = Interlocked.Increment(ref _serversSync);
        server = await vhContext.Servers
            .Include(x => x.AccessPoints)
            .SingleOrDefaultAsync(x => x.ServerId == serverId);

        lock (_serversLock)
        {
            if (sync == _serversSync)
                _servers.TryAdd(serverId, server);
        }

        return server ?? throw new KeyNotFoundException();
    }


    public void InvalidateProject(Guid projectId)
    {
        // clean project cache
        lock (_projectsLock)
        {
            Interlocked.Increment(ref _projectsSync);
            _projects.Remove(projectId);
        }

        // clean servers cache
        lock (_serversLock)
        {
            Interlocked.Increment(ref _serversSync);
            foreach (var item in _servers.Where(x => x.Value?.ProjectId == projectId))
                _servers.Remove(item.Key);
        }

    }

    public void InvalidateServer(Guid serverId)
    {
        // clean servers cache
        lock (_serversLock)
        {
            Interlocked.Increment(ref _serversSync);
            _servers.Remove(serverId);
        }
    }
}
