using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Persistence;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Utils;
using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Agent.Services;

public class CacheService
{
    private class MemCache
    {
        public readonly Dictionary<Guid, Project> Projects = new();
        public ConcurrentDictionary<Guid, Models.ServerModel?>? Servers = new();
        public ConcurrentDictionary<long, Session>? Sessions;
        public ConcurrentDictionary<Guid, Access>? Accesses;
        public ConcurrentDictionary<long, AccessUsageEx> SessionUsages = new();
        public readonly AsyncLock ServersLock = new();
        public readonly AsyncLock ProjectsLock = new();
        public readonly AsyncLock SessionsLock = new();
        public DateTime LastSavedTime = DateTime.MinValue;
    };

    private static MemCache Mem { get; } = new();
    private readonly AgentOptions _appOptions;
    private readonly ILogger<CacheService> _logger;
    private readonly VhContext _vhContext;

    public CacheService(
        IOptions<AgentOptions> appOptions,
        ILogger<CacheService> logger,
        VhContext vhContext)
    {
        _appOptions = appOptions.Value;
        _logger = logger;
        _vhContext = vhContext;
    }

    public async Task<Project?> GetProject(Guid projectId, bool loadFromDb = true)
    {
        using var projectsLock = await Mem.ProjectsLock.LockAsync();

        if (Mem.Projects.TryGetValue(projectId, out var project) || !loadFromDb)
            return project;

        project = await _vhContext.Projects
            .AsNoTracking()
            .SingleAsync(x => x.ProjectId == projectId);
        Mem.Projects.TryAdd(projectId, project);

        return project;
    }

    public async Task<Models.ServerModel?> GetServer(Guid serverId, bool loadFromDb = true)
    {
        using var serversLock = await Mem.ServersLock.LockAsync();

        var servers = await GetServers();
        if (servers.TryGetValue(serverId, out var server) || !loadFromDb)
        {
            if (server != null)
                server.Project ??= await GetProject(server.ProjectId);
            return server;
        }

        server = await _vhContext.Servers
            .Include(x => x.AccessPoints)
            .Include(x => x.ServerStatuses!.Where(serverStatusEx => serverStatusEx.IsLast))
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ServerId == serverId);

        if (server?.ServerStatuses != null)
            server.ServerStatus = server.ServerStatuses.SingleOrDefault();

        if (server != null)
            server.Project ??= await GetProject(server.ProjectId);

        servers.TryAdd(serverId, server);
        return server;
    }

    public async Task<ConcurrentDictionary<Guid, Models.ServerModel?>> GetServers()
    {
        if (Mem.Servers != null)
            return Mem.Servers;

        Mem.Servers = new ConcurrentDictionary<Guid, Models.ServerModel?>();

        await Task.Delay(0);
        return Mem.Servers;
    }

    private async Task<ConcurrentDictionary<Guid, Access>> GetAccesses()
    {
        if (Mem.Accesses != null)
            return Mem.Accesses;

        var accesses = await _vhContext.Sessions
            .Include(x => x.Access)
            .Include(x => x.Access!.AccessToken)
            .Include(x => x.Access!.AccessToken!.AccessPointGroup)
            .Where(x => x.EndTime == null)
            .Select(x => x.Access!)
            .Distinct()
            .AsNoTracking()
            .ToDictionaryAsync(x => x.AccessId);

        Mem.Accesses = new ConcurrentDictionary<Guid, Access>(accesses);
        return Mem.Accesses;

    }

    private async Task<ConcurrentDictionary<long, Session>> GetSessions()
    {
        if (Mem.Sessions != null)
            return Mem.Sessions;

        var sessions = await _vhContext.Sessions
            .Include(x => x.Device)
            .Where(x => x.EndTime == null)
            .AsNoTracking()
            .ToDictionaryAsync(x => x.SessionId);

        // update access from cache
        foreach (var session in sessions.Values)
            session.Access = await GetAccess(session.AccessId);

        Mem.Sessions = new ConcurrentDictionary<long, Session>(sessions);
        return Mem.Sessions;
    }

    public async Task AddSession(Session session)
    {
        using var sessionsLock = await Mem.SessionsLock.LockAsync();

        if (session.Access == null)
            throw new ArgumentException($"{nameof(session.Access)} is not initialized!", nameof(session));

        if (session.Device == null)
            throw new ArgumentException($"{nameof(session.Device)} is not initialized!", nameof(session));

        // check session access
        var cachedAccess = await GetAccess(session.AccessId, false);
        if (cachedAccess == null)
            (await GetAccesses()).TryAdd(session.AccessId, session.Access);
        else if (cachedAccess != session.Access)
            throw new Exception($"Session access should be the same as the one in the cache. SessionId: {session.SessionId}");

        var curSessions = await GetSessions();
        curSessions.TryAdd(session.SessionId, session);
    }

    public async Task<Session> GetSession(long sessionId)
    {
        using var sessionsLock = await Mem.SessionsLock.LockAsync();
        var curSessions = await GetSessions();

        if (!curSessions.TryGetValue(sessionId, out var session))
            throw new KeyNotFoundException();

        return session;
    }

    public async Task<Access?> GetAccessByTokenId(Guid tokenId, Guid? deviceId, bool loadFromDb = true)
    {
        // get from cache
        var accesses = await GetAccesses();
        var access = accesses.Values.FirstOrDefault(x => x.AccessTokenId == tokenId && x.DeviceId == deviceId);
        if (access != null || !loadFromDb)
            return access;

        // multiple requests may be in queued so wait for one to finish then check the cache a
        using var accessLock = await AsyncLock.LockAsync($"Cache_Access_{tokenId}_{deviceId}");
        access = await GetAccessByTokenId(tokenId, deviceId, false);
        if (access != null)
            return access;

        // load from db
        access = await _vhContext.Accesses
            .Include(x => x.AccessToken)
            .Include(x => x.AccessToken!.AccessPointGroup)
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.AccessTokenId == tokenId && x.DeviceId == deviceId);

        if (access != null)
            accesses.TryAdd(access.AccessId, access);
        return access;
    }

    public async Task<Access?> GetAccess(Guid accessId, bool loadFromDb = true)
    {
        var accesses = await GetAccesses();
        if (accesses.TryGetValue(accessId, out var access) || !loadFromDb)
            return access;

        // multiple requests may be in queued so wait for one to finish then check the cache again
        using var accessLock = await AsyncLock.LockAsync($"Cache_AccessId_{accessId}");
        access = await GetAccess(accessId, false);
        if (access != null)
            return access;

        // load from db
        access = await _vhContext.Accesses
            .Include(x => x.AccessToken)
            .Include(x => x.AccessToken!.AccessPointGroup)
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.AccessId == accessId);

        accesses.TryAdd(access!.AccessId, access);
        return access;
    }


    public async Task InvalidateProject(Guid projectId)
    {
        using var projectsLock = await Mem.ProjectsLock.LockAsync();
        using var serversLock = await Mem.ServersLock.LockAsync();

        // clean project cache
        Mem.Projects.Remove(projectId);
        
        // clear project in serverModel
        if (Mem.Servers != null)
            foreach (var server in Mem.Servers.Values.Where(x => x?.ProjectId == projectId))
                server!.Project = null;
    }

    public async Task InvalidateServer(Guid serverId)
    {
        if (Mem.Servers?.TryRemove(serverId, out var oldServer) == true)
        {
            var server = await GetServer(serverId);
            if (server != null)
                server.ServerStatus ??= oldServer?.ServerStatus;
        }
    }

    public void UpdateServer(Models.ServerModel serverModel)
    {
        if (serverModel.AccessPoints == null)
            throw new ArgumentException($"{nameof(serverModel.AccessPoints)} can not be null");

        Mem.Servers?.AddOrUpdate(serverModel.ServerId, serverModel, (_, oldValue) =>
        {
            serverModel.ServerStatus ??= oldValue?.ServerStatus; //restore last status
            return serverModel;
        });
    }

    public AccessUsageEx AddSessionUsage(AccessUsageEx accessUsage)
    {
        if (!Mem.SessionUsages.TryGetValue(accessUsage.SessionId, out var oldUsage))
        {
            Mem.SessionUsages.TryAdd(accessUsage.SessionId, accessUsage);
            return accessUsage;
        }

        oldUsage.ReceivedTraffic += accessUsage.ReceivedTraffic;
        oldUsage.SentTraffic += accessUsage.SentTraffic;
        oldUsage.LastCycleReceivedTraffic = accessUsage.LastCycleReceivedTraffic;
        oldUsage.LastCycleSentTraffic = accessUsage.LastCycleSentTraffic;
        oldUsage.TotalReceivedTraffic = accessUsage.TotalReceivedTraffic;
        oldUsage.TotalSentTraffic = accessUsage.TotalSentTraffic;
        oldUsage.CreatedTime = accessUsage.CreatedTime;
        return oldUsage;
    }

    public async Task SaveChanges(bool force = false)
    {
        using var sessionsLock = await Mem.SessionsLock.LockAsync();
        _vhContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));

        var savingTime = DateTime.UtcNow;
        var minCacheTime = force ? DateTime.MaxValue : savingTime - _appOptions.SessionCacheTimeout;
        var minSessionTime = savingTime - _appOptions.SessionTimeout;

        // find updated sessions
        var curSessions = await GetSessions();
        var updatedSessions = curSessions.Values
            .Where(x =>
                x.EndTime == null && x.AccessedTime > Mem.LastSavedTime && x.AccessedTime <= savingTime ||
                x.EndTime != null && !x.IsEndTimeSaved)
            .ToList();

        // close and update timeout sessions
        var timeoutSessions = curSessions.Values.Where(x => x.EndTime == null && x.AccessedTime < minSessionTime);
        foreach (var session in timeoutSessions)
        {
            session.EndTime = session.AccessedTime;
            session.ErrorCode = SessionErrorCode.SessionClosed;
            session.ErrorMessage = "timeout";
            if (!updatedSessions.Contains(session))
                updatedSessions.Add(session);
        }

        // update sessions
        foreach (var session in updatedSessions)
        {
            var entry = _vhContext.Sessions.Attach(new Session(session.SessionId)
            {
                AccessedTime = session.AccessedTime,
                EndTime = session.EndTime,
            });
            entry.Property(x => x.AccessedTime).IsModified = true;
            entry.Property(x => x.EndTime).IsModified = true;
        }

        // update accesses
        var accesses = updatedSessions
            .Select(x => x.Access)
            .DistinctBy(x => x!.AccessId)
            .Select(x => new Access(x!.AccessId)
            {
                AccessedTime = x.AccessedTime,
                TotalReceivedTraffic = x.TotalReceivedTraffic,
                TotalSentTraffic = x.TotalSentTraffic
            });
        foreach (var access in accesses)
        {
            var entry = _vhContext.Accesses.Attach(access);
            entry.Property(x => x.AccessedTime).IsModified = true;
            entry.Property(x => x.TotalReceivedTraffic).IsModified = true;
            entry.Property(x => x.TotalSentTraffic).IsModified = true;
        }

        // save updated sessions
        try
        {
            await _vhContext.SaveChangesAsync();

            // set IsEndTimeSaved to make sure it doesn't try to update them again because closed session maybe archived
            foreach (var closedSession in updatedSessions.Where(x => x.EndTime != null))
                closedSession.IsEndTimeSaved = true;
        }
        catch (Exception ex)
        {
            foreach (var closedSession in curSessions.Where(x => x.Value.EndTime != null).ToArray())
                curSessions.TryRemove(closedSession.Key, out _);

            _logger.LogError(ex, "Could not flush sessions! All closed session in cache has been discarded.");
        }

        // save access usages
        var sessionUsages = Mem.SessionUsages.Values.ToArray();
        Mem.SessionUsages = new ConcurrentDictionary<long, AccessUsageEx>();
        try
        {
            await _vhContext.AccessUsages.AddRangeAsync(sessionUsages);
            await _vhContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not write AccessUsages! All access Usage has been discarded.");
        }

        // remove old access
        var allAccesses = await GetAccesses();
        foreach (var access in allAccesses.Values.Where(x => x.AccessedTime < minCacheTime))
            allAccesses.TryRemove(access.AccessId, out _);

        // ServerStatus
        await SaveServerStatus();

        // remove closed sessions
        var unusedSession = curSessions.Where(x =>
            x.Value.EndTime != null &&
            x.Value.AccessedTime < minCacheTime);
        foreach (var session in unusedSession)
            curSessions.TryRemove(session.Key, out _);

        Mem.LastSavedTime = savingTime;
    }

    public async Task SaveServerStatus()
    {
        using var serversLock = await Mem.ServersLock.LockAsync();

        var servers = await GetServers();
        var serverStatuses = servers.Values.Select(x => x?.ServerStatus)
            .Where(x => x?.CreatedTime > Mem.LastSavedTime)
            .Select(x => x!)
            .ToArray();

        serverStatuses = serverStatuses.Select(x => new ServerStatusEx()
        {
            ServerStatusId = 0,
            IsLast = true,
            CreatedTime = x.CreatedTime,
            FreeMemory = x.FreeMemory,
            ServerId = x.ServerId,
            IsConfigure = x.IsConfigure,
            ProjectId = x.ProjectId,
            SessionCount = x.SessionCount,
            TcpConnectionCount = x.TcpConnectionCount,
            UdpConnectionCount = x.UdpConnectionCount,
            ThreadCount = x.ThreadCount,
            TunnelReceiveSpeed = x.TunnelReceiveSpeed,
            TunnelSendSpeed = x.TunnelSendSpeed
        }).ToArray();

        if (!serverStatuses.Any())
            return;

        await using var transaction = _vhContext.Database.CurrentTransaction == null ? await _vhContext.Database.BeginTransactionAsync() : null;

        // remove isLast
        var serverIds = string.Join(',', serverStatuses.Select(x => $"'{x.ServerId}'"));
        var sql =
            $"UPDATE {nameof(_vhContext.ServerStatuses)} " +
            $"SET {nameof(ServerStatusEx.IsLast)} = 0 " +
            $"WHERE {nameof(ServerStatusEx.ServerId)} in ({serverIds}) and {nameof(ServerStatusEx.IsLast)} = 1";
        await _vhContext.Database.ExecuteSqlRawAsync(sql);

        // save new statuses
        await _vhContext.ServerStatuses.AddRangeAsync(serverStatuses);

        // commit changes
        await _vhContext.SaveChangesAsync();
        if (transaction != null)
            await _vhContext.Database.CommitTransactionAsync();

        // remove old servers from the cache
        var oldServers = servers
            .Where(x => x.Value == null || x.Value.ServerStatus?.CreatedTime < DateTime.UtcNow - TimeSpan.FromDays(1))
            .ToArray();
        foreach (var oldServer in oldServers)
            servers.TryRemove(oldServer);
    }

    public async Task<Session[]> GetActiveSessions(Guid accessId)
    {
        using var sessionsLock = await Mem.SessionsLock.LockAsync();

        var curSessions = await GetSessions();
        var ret = curSessions.Values
            .Where(x => x.EndTime == null && x.AccessId == accessId)
            .OrderBy(x => x.CreatedTime).ToArray();

        return ret;
    }

    public Task InvalidateSessions()
    {
        Mem.Sessions = null;
        Mem.Accesses = null;
        Mem.SessionUsages = new ConcurrentDictionary<long, AccessUsageEx>();
        return Task.CompletedTask;
    }
}
