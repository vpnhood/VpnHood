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
        public readonly Dictionary<Guid, ProjectModel> Projects = new();
        public ConcurrentDictionary<Guid, ServerModel?>? Servers;
        public ConcurrentDictionary<long, SessionModel>? Sessions;
        public ConcurrentDictionary<Guid, AccessModel>? Accesses;
        public ConcurrentDictionary<long, AccessUsageModel> SessionUsages = new();
        public readonly AsyncLock ServersLoadLock = new();
        public readonly AsyncLock ServersLock = new();
        public readonly AsyncLock ProjectsLock = new();
        public DateTime LastSavedTime = DateTime.MinValue;
    }

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

    public async Task<ProjectModel?> GetProject(Guid projectId, bool loadFromDb = true)
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

    public async Task<ServerModel?> GetServer(Guid serverId, bool loadFromDb = true)
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

    public async Task<ConcurrentDictionary<Guid, ServerModel?>> GetServers()
    {
        //using var serversLock = await Mem.ServersLoadLock.LockAsync();
        if (Mem.Servers != null)
            return Mem.Servers;

        //todo bulk load
        Mem.Servers = new ConcurrentDictionary<Guid, ServerModel?>();
        await Task.Delay(0);

        ////load recent servers
        //var minCreatedTime = DateTime.UtcNow - _appOptions.ServerUpdateStatusInterval * 3;
        //var servers = await _vhContext.ServerStatuses
        //    .Where(x => x.IsLast && x.CreatedTime > minCreatedTime)
        //    .Select(x => x.Server)
        //    .ToDictionaryAsync(server => server!.ServerId);

        //Mem.Servers = new ConcurrentDictionary<Guid, ServerModel?>(servers);
        return Mem.Servers;
    }

    private async Task<ConcurrentDictionary<Guid, AccessModel>> GetAccesses()
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

        Mem.Accesses = new ConcurrentDictionary<Guid, AccessModel>(accesses);
        return Mem.Accesses;

    }

    private async Task<ConcurrentDictionary<long, SessionModel>> GetSessions()
    {
        if (Mem.Sessions != null)
            return Mem.Sessions;

        var sessions = await _vhContext.Sessions
            .Include(x => x.Device)
            .Where(x => !x.IsArchived)
            .AsNoTracking()
            .ToDictionaryAsync(x => x.SessionId);

        // update access from cache
        foreach (var session in sessions.Values)
            session.Access = await GetAccess(session.AccessId);

        Mem.Sessions = new ConcurrentDictionary<long, SessionModel>(sessions);
        return Mem.Sessions;
    }

    public async Task AddSession(SessionModel session)
    {
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

    public async Task<SessionModel> GetSession(Guid? serverId, long sessionId)
    {
        var curSessions = await GetSessions();
        if (!curSessions.TryGetValue(sessionId, out var session))
            throw new KeyNotFoundException();

        // server validation
        if (serverId != null && session.ServerId != serverId)
            throw new KeyNotFoundException();

        return session;
    }

    public async Task<AccessModel?> GetAccessByTokenId(Guid tokenId, Guid? deviceId, bool loadFromDb = true)
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

    public async Task<AccessModel?> GetAccess(Guid accessId, bool loadFromDb = true)
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


        // make sure to return the reference
        return access != null ? accesses.GetOrAdd(accessId, access) : null;
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

    public async Task InvalidateProjectServers(Guid projectId)
    {
        if (Mem.Servers == null)
            return;

        foreach (var server in Mem.Servers.Values.Where(x => x != null && x.ProjectId == projectId))
        {
            await Task.Delay(100); // don't put pressure on db
            await InvalidateServer(server!.ServerId);
        }
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

    public void UpdateServer(ServerModel serverModel)
    {
        if (serverModel.AccessPoints == null)
            throw new ArgumentException($"{nameof(serverModel.AccessPoints)} can not be null");

        Mem.Servers?.AddOrUpdate(serverModel.ServerId, serverModel, (_, oldValue) =>
        {
            serverModel.ServerStatus ??= oldValue?.ServerStatus; //restore last status
            return serverModel;
        });
    }

    public void AddSessionUsage(AccessUsageModel accessUsage)
    {
        if (accessUsage.ReceivedTraffic + accessUsage.SentTraffic == 0)
            return;

        if (!Mem.SessionUsages.TryGetValue(accessUsage.SessionId, out var oldUsage))
        {
            Mem.SessionUsages.TryAdd(accessUsage.SessionId, accessUsage);
            return;
        }

        oldUsage.ReceivedTraffic += accessUsage.ReceivedTraffic;
        oldUsage.SentTraffic += accessUsage.SentTraffic;
        oldUsage.LastCycleReceivedTraffic = accessUsage.LastCycleReceivedTraffic;
        oldUsage.LastCycleSentTraffic = accessUsage.LastCycleSentTraffic;
        oldUsage.TotalReceivedTraffic = accessUsage.TotalReceivedTraffic;
        oldUsage.TotalSentTraffic = accessUsage.TotalSentTraffic;
        oldUsage.CreatedTime = accessUsage.CreatedTime;
    }

    private void UpdateTimeoutSessions(ConcurrentDictionary<long, SessionModel> sessions, Dictionary<long, SessionModel> updatedSessions)
    {
        var minSessionTime = DateTime.UtcNow - _appOptions.SessionTimeout;
        var timeoutSessions = sessions.Values
            .Where(sessionModel =>
                sessionModel.EndTime == null &&
                sessionModel.LastUsedTime < minSessionTime);

        foreach (var session in timeoutSessions)
        {
            if (session.ErrorCode != SessionErrorCode.Ok) _logger.LogWarning(
                "Session has error but it has not been closed. SessionId: {SessionId}", session.SessionId);

            session.EndTime = session.LastUsedTime;
            session.ErrorCode = SessionErrorCode.SessionClosed;
            session.ErrorMessage = "timeout";
            updatedSessions.TryAdd(session.SessionId, session);
        }

        // archive old sessions
        foreach (var session in sessions.Values.Where(x => x.LastUsedTime < minSessionTime))
        {
            session.IsArchived = true;
            updatedSessions.TryAdd(session.SessionId, session);
        }
    }

    public async Task SaveChanges()
    {
        _vhContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
        var savingTime = DateTime.UtcNow;
        var sessions = await GetSessions();
        _vhContext.ChangeTracker.Clear();

        // find updated sessions
        var updatedSessions = sessions.Values
            .Where(session => session.LastUsedTime > Mem.LastSavedTime || session.EndTime > Mem.LastSavedTime)
            .ToDictionary(x => x.SessionId);

        UpdateTimeoutSessions(sessions, updatedSessions);

        // update sessions
        foreach (var session in updatedSessions.Values)
        {
            var entry = _vhContext.Sessions.Attach(session.Clone());
            entry.Property(x => x.LastUsedTime).IsModified = true;
            entry.Property(x => x.EndTime).IsModified = true;
            entry.Property(x => x.SuppressedTo).IsModified = true;
            entry.Property(x => x.SuppressedBy).IsModified = true;
            entry.Property(x => x.ErrorMessage).IsModified = true;
            entry.Property(x => x.ErrorCode).IsModified = true;
            entry.Property(x => x.IsArchived).IsModified = true;
        }

        // update accesses
        var accesses = updatedSessions.Values
            .Select(x => x.Access)
            .DistinctBy(x => x!.AccessId)
            .Select(access => access!.Clone());
        foreach (var access in accesses)
        {
            var entry = _vhContext.Accesses.Attach(access);
            entry.Property(x => x.LastUsedTime).IsModified = true;
            entry.Property(x => x.TotalReceivedTraffic).IsModified = true;
            entry.Property(x => x.TotalSentTraffic).IsModified = true;
        }

        // save updated sessions
        try
        {
            await _vhContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not flush sessions! All archived sessions in cache has been discarded.");
        }
        finally
        {
            // remove archived sessions to make sure not to update them again
            foreach (var session in sessions.Values.Where(session => session.IsArchived))
                sessions.TryRemove(session.SessionId, out _);

            // remove unused accesses
            var minSessionTime = DateTime.UtcNow - _appOptions.SessionTimeout;
            var allAccesses = await GetAccesses();
            foreach (var access in allAccesses.Values.Where(x => x.LastUsedTime < minSessionTime))
                allAccesses.TryRemove(access.AccessId, out _);
        }

        // save access usages
        var sessionUsages = Mem.SessionUsages.Values.ToArray();
        Mem.SessionUsages = new ConcurrentDictionary<long, AccessUsageModel>();
        try
        {
            await _vhContext.AccessUsages.AddRangeAsync(sessionUsages);
            await _vhContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not write AccessUsages! All access Usage has been discarded.");
        }

        // ServerStatus
        try
        {
            await SaveServersStatus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not save servers status!");
        }

        Mem.LastSavedTime = savingTime;
    }

    private static string ToSqlValue<T>(T? value)
    {
        return value?.ToString() ?? "NULL";
    }

    public async Task SaveServersStatus()
    {
        using var serversLock = await Mem.ServersLock.LockAsync();

        var servers = await GetServers();
        var serverStatuses = servers.Values.Select(x => x?.ServerStatus)
            .Where(x => x?.CreatedTime > Mem.LastSavedTime)
            .Select(x => x!)
            .ToArray();

        serverStatuses = serverStatuses.Select(x => new ServerStatusModel
        {
            IsLast = true,
            CreatedTime = x.CreatedTime,
            AvailableMemory = x.AvailableMemory,
            CpuUsage = x.CpuUsage,
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

        // remove isLast
        var serverIds = string.Join(',', serverStatuses.Select(x => $"'{x.ServerId}'"));
        var sql =
            $"UPDATE {nameof(_vhContext.ServerStatuses)} " +
            $"SET {nameof(ServerStatusModel.IsLast)} = 0 " +
            $"WHERE {nameof(ServerStatusModel.ServerId)} in ({serverIds}) and {nameof(ServerStatusModel.IsLast)} = 1;";

        // save new statuses
        var values = serverStatuses.Select(x => "\r\n(" +
            $"{(x.IsLast ? 1 : 0)}, '{x.CreatedTime:yyyy-MM-dd HH:mm:ss.fff}', {ToSqlValue(x.AvailableMemory)}, {ToSqlValue(x.CpuUsage)}, " +
            $"'{x.ServerId}', {(x.IsConfigure ? 1 : 0)}, '{x.ProjectId}', " +
            $"{x.SessionCount}, {x.TcpConnectionCount}, {x.UdpConnectionCount}, " +
            $"{x.ThreadCount}, {x.TunnelReceiveSpeed}, {x.TunnelSendSpeed}" +
            ")");

        sql +=
            $"\r\nINSERT INTO {nameof(_vhContext.ServerStatuses)} (" +
            $"{nameof(ServerStatusModel.IsLast)}, {nameof(ServerStatusModel.CreatedTime)}, {nameof(ServerStatusModel.AvailableMemory)}, {nameof(ServerStatusModel.CpuUsage)}, " +
            $"{nameof(ServerStatusModel.ServerId)}, {nameof(ServerStatusModel.IsConfigure)}, {nameof(ServerStatusModel.ProjectId)}, " +
            $"{nameof(ServerStatusModel.SessionCount)}, {nameof(ServerStatusModel.TcpConnectionCount)},{nameof(ServerStatusModel.UdpConnectionCount)}, " +
            $"{nameof(ServerStatusModel.ThreadCount)}, {nameof(ServerStatusModel.TunnelReceiveSpeed)}, {nameof(ServerStatusModel.TunnelSendSpeed)}" +
            ") " +
            $"VALUES {string.Join(',', values)}";
        await _vhContext.Database.ExecuteSqlRawAsync(sql);

        //await using var transaction = _vhContext.Database.CurrentTransaction == null ? await _vhContext.Database.BeginTransactionAsync() : null;
        //await _vhContext.Database.ExecuteSqlRawAsync(sql);
        //await _vhContext.ServerStatuses.AddRangeAsync(serverStatuses); // i couldn't understand what it doesn't work on production

        // commit changes
        //await _vhContext.SaveChangesAsync();
        //if (transaction != null)
        //await _vhContext.Database.CommitTransactionAsync();

        // remove old servers from the cache
        var oldServers = servers
            .Where(x => x.Value == null || x.Value.ServerStatus?.CreatedTime < DateTime.UtcNow - TimeSpan.FromDays(1))
            .ToArray();
        foreach (var oldServer in oldServers)
            servers.TryRemove(oldServer);
    }

    public async Task<SessionModel[]> GetActiveSessions(Guid accessId)
    {
        var sessions = await GetSessions();
        var ret = sessions.Values
            .Where(x => x.EndTime == null && x.AccessId == accessId)
            .OrderBy(x => x.CreatedTime).ToArray();

        return ret;
    }

    public Task InvalidateSessions()
    {
        Mem.Sessions = null;
        Mem.Accesses = null;
        Mem.SessionUsages = new ConcurrentDictionary<long, AccessUsageModel>();
        return Task.CompletedTask;
    }
}
