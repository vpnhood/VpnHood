using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Caching;

public class SystemCache
{
    private readonly Dictionary<Guid, Project> _projects = new();
    private readonly Dictionary<Guid, Models.Server?> _servers = new();
    private readonly Dictionary<long, Session?> _sessions = new();
    private readonly List<AccessUsageEx> _accessUsages = new();
    private readonly AsyncLock _serversLock = new();
    private readonly AsyncLock _projectsLock = new();
    private readonly AsyncLock _sessionsLock = new();
    private readonly AsyncLock _saveSessionsLock = new();
    private DateTime _lastSavedTime = DateTime.MinValue;

    public async Task<Project> GetProject(VhContext vhContext, Guid projectId)
    {
        using var projectsLock = await _projectsLock.LockAsync();

        if (_projects.TryGetValue(projectId, out var project))
            return project;

        project = await vhContext.Projects.SingleAsync(x => x.ProjectId == projectId);
        _projects.TryAdd(projectId, project);

        return project;
    }

    public async Task<Models.Server> GetServer(VhContext vhContext, Guid serverId)
    {
        using var serversLock = await _serversLock.LockAsync();

        if (_servers.TryGetValue(serverId, out var server))
            return server ?? throw new KeyNotFoundException();

        server = await vhContext.Servers
            .Include(x => x.AccessPoints)
            .SingleOrDefaultAsync(x => x.ServerId == serverId);

        _servers.TryAdd(serverId, server);

        return server ?? throw new KeyNotFoundException();
    }

    public async Task<Session> GetSession(VhContext vhContext, long sessionId)
    {
        using var sessionsLock = await _sessionsLock.LockAsync();

        if (_sessions.TryGetValue(sessionId, out var session))
            return session ?? throw new KeyNotFoundException();

        session = await vhContext.Sessions
            .Include(x => x.Access)
            .Include(x => x.Access!.AccessToken)
            .Include(x => x.Access!.AccessToken!.AccessPointGroup)
            .Include(x => x.Device)
            .SingleOrDefaultAsync(x => x.SessionId == sessionId);

        // update reference to prevent duplicate id in different reference. It is for updating later
        if (session != null)
        {
            session.Access =
                _sessions.Values.FirstOrDefault(x => x?.AccessId == session.AccessId)?.Access
                ?? session.Access;

            session.Access!.AccessToken =
                _sessions.Values.FirstOrDefault(x => x?.Access!.AccessTokenId == session.Access.AccessTokenId)?.Access!.AccessToken
                ?? session.Access!.AccessToken;

            session.Access!.AccessToken!.AccessPointGroup =
                _sessions.Values.FirstOrDefault(x => x?.Access!.AccessToken!.AccessPointGroupId == session.Access.AccessToken.AccessPointGroupId)?.Access!.AccessToken!.AccessPointGroup
                ?? session.Access!.AccessToken.AccessPointGroup;

            session.Device =
                _sessions.Values.FirstOrDefault(x => x?.DeviceId == session.DeviceId)?.Device ??
                session.Device;

            session.Access!.Device =
                _sessions.Values.FirstOrDefault(x => x?.DeviceId == session.Access.DeviceId)?.Device ??
                _sessions.Values.FirstOrDefault(x => x?.Access!.DeviceId == session.Access.DeviceId)?.Access!.Device ??
                session.Access!.Device;

        }

        _sessions.TryAdd(sessionId, session);
        return session ?? throw new KeyNotFoundException();
    }


    public async Task InvalidateProject(Guid projectId)
    {
        using var projectsLock = await _projectsLock.LockAsync();
        using var serversLock = await _serversLock.LockAsync();

        // clean project cache
        _projects.Remove(projectId);

        // clean servers cache
        foreach (var item in _servers.Where(x => x.Value?.ProjectId == projectId))
            _servers.Remove(item.Key);

    }

    public async Task InvalidateServer(Guid serverId)
    {
        using var serversLock = await _serversLock.LockAsync();

        // clean servers cache
        _servers.Remove(serverId);
    }

    public async Task InvalidateSession(long sessionId)
    {
        using var sessionsLock = await _sessionsLock.LockAsync();

        _sessions.Remove(sessionId);
        lock (_accessUsages)
            _accessUsages.RemoveAll(x => x.SessionId == sessionId);

    }

    public async Task InvalidateSessions()
    {
        using var sessionsLock = await _sessionsLock.LockAsync();
        
        _sessions.Clear();
        lock (_accessUsages)
            _accessUsages.Clear();
    }


    public AccessUsageEx AddAccessUsage(AccessUsageEx accessUsage)
    {
        lock (_accessUsages)
        {
            var oldUsage = _accessUsages.SingleOrDefault(x => x.SessionId == accessUsage.SessionId);
            if (oldUsage != null)
            {
                oldUsage.ReceivedTraffic += accessUsage.ReceivedTraffic;
                oldUsage.SentTraffic += accessUsage.SentTraffic;
                oldUsage.CycleReceivedTraffic = accessUsage.CycleReceivedTraffic;
                oldUsage.CycleSentTraffic = accessUsage.CycleSentTraffic;
                oldUsage.TotalReceivedTraffic = accessUsage.TotalReceivedTraffic;
                oldUsage.TotalSentTraffic = accessUsage.TotalSentTraffic;
                oldUsage.CreatedTime = accessUsage.CreatedTime;
                accessUsage = oldUsage;
            }

            _accessUsages.Add(accessUsage);
            return accessUsage;
        }
    }

    public async Task SaveChanges(VhContext vhContext)
    {
        using var saveSessionsLock = await _saveSessionsLock.LockAsync();
        using var sessionsLock = await _sessionsLock.LockAsync();

        var savedTime = DateTime.UtcNow;

        // save sessions
        var sessions = _sessions.Values.Where(x => x != null && x.AccessedTime > _lastSavedTime && x.AccessedTime <= savedTime);
        foreach (var session in sessions)
        {
            if (session == null) continue;
            vhContext.Sessions.Attach(session);
            vhContext.Entry(session).Property(x => x.AccessedTime).IsModified = true;
            vhContext.Entry(session).Property(x => x.EndTime).IsModified = true;
            vhContext.Entry(session.Access!).Property(x => x.AccessedTime).IsModified = true;
            vhContext.Entry(session.Access!).Property(x => x.CycleReceivedTraffic).IsModified = true;
            vhContext.Entry(session.Access!).Property(x => x.CycleSentTraffic).IsModified = true;
            vhContext.Entry(session.Access!).Property(x => x.TotalReceivedTraffic).IsModified = true;
            vhContext.Entry(session.Access!).Property(x => x.TotalSentTraffic).IsModified = true;
        }

        // add access usages
        AccessUsageEx[] accessUsages;
        lock (_accessUsages)
            accessUsages = _accessUsages.ToArray();
        await vhContext.AccessUsages.AddRangeAsync(accessUsages);

        // commit
        await vhContext.SaveChangesAsync();

        // remove access usages
        lock (_accessUsages)
            _accessUsages.RemoveRange(0, accessUsages.Length);

        _lastSavedTime = savedTime;
    }

    public Task<IDisposable> LockSaveSessions()
    {
        return _saveSessionsLock.LockAsync();
    }
}
