using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Models;
using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Caching;

public class SystemCache
{
    private readonly IOptions<AppOptions> _appOptions;
    private readonly ILogger<SystemCache> _logger;
    private readonly Dictionary<Guid, Project> _projects = new();
    private readonly Dictionary<Guid, Models.Server?> _servers = new();
    private readonly List<AccessUsageEx> _accessUsages = new();
    private readonly AsyncLock _serversLock = new();
    private readonly AsyncLock _projectsLock = new();
    private readonly AsyncLock _sessionsLock = new();
    private DateTime _lastSavedTime = DateTime.MinValue;
    private Dictionary<long, Session>? _sessions = new();

    public SystemCache(IOptions<AppOptions> appOptions, ILogger<SystemCache> logger)
    {
        _appOptions = appOptions;
        _logger = logger;
    }

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

    private async Task<Dictionary<long, Session>> GetSessions(VhContext vhContext)
    {
        if (_sessions != null)
            return _sessions;

        _sessions = await vhContext.Sessions
            .Include(x => x.Access)
            .Include(x => x.Access!.AccessToken)
            .Include(x => x.Access!.AccessToken!.AccessPointGroup)
            .Include(x => x.Device)
            .AsNoTracking()
            .Where(x => x.EndTime == null)
            .ToDictionaryAsync(x => x.SessionId);

        return _sessions;
    }

    public async Task AddSession(VhContext vhContext, Session session)
    {
        using var sessionsLock = await _sessionsLock.LockAsync();

        if (session.Access == null) throw new ArgumentException($"{nameof(session.Access)} is not initialized!", nameof(session));
        if (session.Access.AccessToken == null) throw new ArgumentException($"{nameof(session.Access.AccessToken)} is not initialized!", nameof(session));
        if (session.Access.AccessToken.AccessPointGroup == null) throw new ArgumentException($"{nameof(session.Access.AccessToken.AccessPointGroup)} is not initialized!", nameof(session));
        if (session.Device == null) throw new ArgumentException($"{nameof(session.Device)} is not initialized!", nameof(session));

        var curSessions = await GetSessions(vhContext);
        curSessions.Add(session.SessionId, session);
    }

    public async Task<Session> GetSession(VhContext vhContext, long sessionId)
    {
        using var sessionsLock = await _sessionsLock.LockAsync();
        var curSessions = await GetSessions(vhContext);

        if (!curSessions.TryGetValue(sessionId, out var session))
            throw new KeyNotFoundException();

        return session;
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

    public AccessUsageEx AddAccessUsage(AccessUsageEx accessUsage)
    {
        lock (_accessUsages)
        {
            var oldUsage = _accessUsages.SingleOrDefault(x => x.SessionId == accessUsage.SessionId);
            if (oldUsage == null)
            {
                _accessUsages.Add(accessUsage);
                return accessUsage;
            }

            oldUsage.ReceivedTraffic += accessUsage.ReceivedTraffic;
            oldUsage.SentTraffic += accessUsage.SentTraffic;
            oldUsage.CycleReceivedTraffic = accessUsage.CycleReceivedTraffic;
            oldUsage.CycleSentTraffic = accessUsage.CycleSentTraffic;
            oldUsage.TotalReceivedTraffic = accessUsage.TotalReceivedTraffic;
            oldUsage.TotalSentTraffic = accessUsage.TotalSentTraffic;
            oldUsage.CreatedTime = accessUsage.CreatedTime;
            return oldUsage;
        }
    }

    public async Task SaveChanges(VhContext vhContext)
    {
        using var sessionsLock = await _sessionsLock.LockAsync();

        var savedTime = DateTime.UtcNow;

        // find updated sessions
        var curSessions = await GetSessions(vhContext);
        var updatedSessions = curSessions.Values
            .Where(x => 
                (x.EndTime == null && x.AccessedTime > _lastSavedTime && x.AccessedTime <= savedTime) ||
                (x.EndTime != null && x.EndTime > _lastSavedTime && x.EndTime <= savedTime))
            .Select(x => x)
            .ToList();

        // close and update timeout sessions
        var timeoutTime = savedTime - _appOptions.Value.SessionTimeout;
        var timeoutSessions = curSessions.Values.Where(x => x.EndTime == null && x.AccessedTime < timeoutTime);
        foreach (var session in timeoutSessions)
        {
            session.EndTime = session.AccessedTime;
            session.ErrorCode = SessionErrorCode.SessionClosed;
            session.ErrorMessage = "timeout";
            if (!updatedSessions.Contains(session))
                updatedSessions.Add(session);
        }

        // update sessions
        var newSessions = updatedSessions.Select(x => new Session(x.SessionId)
        {
            AccessedTime = x.AccessedTime,
            EndTime = x.EndTime
        });
        foreach (var session in newSessions)
        {
            var entry = vhContext.Sessions.Attach(session);
            entry.Property(x => x.AccessedTime).IsModified = true;
            entry.Property(x => x.EndTime).IsModified = true;
        }

        // update accesses
        var accesses = updatedSessions.Select(x => x.Access).DistinctBy(x => x!.AccessId).Select(x => new Access(x!.AccessId)
        {
            AccessedTime = x.AccessedTime,
            CycleReceivedTraffic = x.CycleReceivedTraffic,
            CycleSentTraffic = x.CycleSentTraffic,
            TotalReceivedTraffic = x.TotalReceivedTraffic,
            TotalSentTraffic = x.TotalSentTraffic
        });
        foreach (var access in accesses)
        {
            var entry = vhContext.Accesses.Attach(access);
            entry.Property(x => x.AccessedTime).IsModified = true;
            entry.Property(x => x.CycleReceivedTraffic).IsModified = true;
            entry.Property(x => x.CycleSentTraffic).IsModified = true;
            entry.Property(x => x.TotalReceivedTraffic).IsModified = true;
            entry.Property(x => x.TotalSentTraffic).IsModified = true;
        }

        // save updated sessions
        try
        {
            await vhContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not write Updated Sessions! Lets hope buggy record clear out.");
        }

        // add access usages. 
        AccessUsageEx[] accessUsages;
        lock (_accessUsages)
            accessUsages = _accessUsages.ToArray();
        try
        {
            await vhContext.AccessUsages.AddRangeAsync(accessUsages);
            await vhContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not write AccessUsages! All access Usage has been dropped.");
        }

        // remove access usages
        lock (_accessUsages)
            _accessUsages.RemoveRange(0, accessUsages.Length);

        // remove closed sessions
        var unusedSession = curSessions.Where(x =>
            x.Value.EndTime != null &&
            DateTime.UtcNow - x.Value.AccessedTime > _appOptions.Value.SessionCacheTimeout);
        foreach (var session in unusedSession)
            curSessions.Remove(session.Key);

        _lastSavedTime = savedTime;
    }

    public async Task<Session[]> GetActiveSessions(VhContext vhContext, Guid accessId)
    {
        using var sessionsLock = await _sessionsLock.LockAsync();

        var curSessions = await GetSessions(vhContext);
        var ret = curSessions.Values
            .Where(x => x.EndTime == null && x.AccessId == accessId)
            .OrderBy(x => x.CreatedTime).ToArray();

        return ret;
    }

    public async Task ResetCycleTraffics(VhContext vhContext)
    {
        using var sessionsLock = await _sessionsLock.LockAsync();
        var curSessions = await GetSessions(vhContext);
        var accesses = curSessions.Values.Select(x => x.Access!);
        foreach (var access in accesses)
        {
            access.CycleReceivedTraffic = 0;
            access.CycleSentTraffic = 0;
        }
    }
}
