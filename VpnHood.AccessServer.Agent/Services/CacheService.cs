using System.Collections.Concurrent;
using GrayMint.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Persistence;
using VpnHood.AccessServer.Models;
using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Agent.Services;

public class CacheService
{
    private class MemCache
    {
        public ConcurrentDictionary<Guid, ProjectModel>? Projects;
        public ConcurrentDictionary<Guid, ServerModel?>? Servers;
        public ConcurrentDictionary<long, SessionModel>? Sessions;
        public ConcurrentDictionary<Guid, AccessModel>? Accesses;
        public ConcurrentDictionary<long, AccessUsageModel> SessionUsages = new();
        public readonly AsyncLock ProjectsLock = new();
        public readonly AsyncLock ServersLock = new();
        public readonly AsyncLock SessionsLock = new();
        public readonly AsyncLock AccessesLock = new();
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

    public async Task<ConcurrentDictionary<Guid, ProjectModel>> GetProjects()
    {
        var servers = await GetServers();

        using var projectsLock = await Mem.ProjectsLock.LockAsync();
        if (Mem.Projects != null)
            return Mem.Projects;

        var projects = servers.Values
           .Where(server => server != null)
           .Select(server => server!.Project!)
           .DistinctBy(project => project.ProjectId)
           .ToDictionary(project => project.ProjectId);

        Mem.Projects = new ConcurrentDictionary<Guid, ProjectModel>(projects);
        return Mem.Projects;
    }

    public async Task<ConcurrentDictionary<Guid, ServerModel?>> GetServers()
    {
        using var serversLock = await Mem.ServersLock.LockAsync();
        if (Mem.Servers != null)
            return Mem.Servers;

        //load recent servers
        var minCreatedTime = DateTime.UtcNow - TimeSpan.FromDays(1);
        var servers = await _vhContext.ServerStatuses
            .Include(serverStatus => serverStatus.Project)
            .Where(serverStatus => serverStatus.IsLast && serverStatus.CreatedTime > minCreatedTime)
            .Select(serverStatus => serverStatus.Server)
            .ToDictionaryAsync(server => server!.ServerId);

        Mem.Servers = new ConcurrentDictionary<Guid, ServerModel?>(servers);
        return Mem.Servers;
    }

    private async Task<ConcurrentDictionary<Guid, AccessModel>> GetAccesses()
    {
        var sessions = await GetSessions();

        using var accessesLock = await Mem.AccessesLock.LockAsync();
        if (Mem.Accesses != null)
            return Mem.Accesses;

        var accesses = sessions.Values
            .Select(session => session.Access!)
            .DistinctBy(access => access.AccessId)
            .ToDictionary(access => access.AccessId);

        Mem.Accesses = new ConcurrentDictionary<Guid, AccessModel>(accesses);
        return Mem.Accesses;

    }

    private async Task<ConcurrentDictionary<long, SessionModel>> GetSessions()
    {
        using var sessionsLock = await Mem.SessionsLock.LockAsync();
        if (Mem.Sessions != null)
            return Mem.Sessions;

        _logger.LogInformation("Loading old accesses and sessions...");
        var sessions = await _vhContext.Sessions
            .Include(session => session.Device)
            .Include(session => session.Access)
            .Include(session => session.Access!.AccessToken)
            .Include(session => session.Access!.AccessToken!.AccessPointGroup)
            .Where(x => !x.IsArchived)
            .AsNoTracking()
            .ToDictionaryAsync(x => x.SessionId);

        Mem.Sessions = new ConcurrentDictionary<long, SessionModel>(sessions);
        return Mem.Sessions;
    }


    public async Task<ProjectModel?> GetProject(Guid projectId, bool loadFromDb = true)
    {
        var projects = await GetProjects();
        if (projects.TryGetValue(projectId, out var project) || !loadFromDb)
            return project;

        using var projectsLock = await AsyncLock.LockAsync($"Cache_project_{projectId}");
        project = await GetProject(projectId, false);
        if (project != null)
            return project;

        project = await _vhContext.Projects
            .AsNoTracking()
            .SingleAsync(x => x.ProjectId == projectId);

        project = projects.GetOrAdd(projectId, project);
        return project;
    }

    public async Task<ServerModel?> GetServer(Guid serverId, bool loadFromDb = true)
    {
        var servers = await GetServers();
        if (servers.TryGetValue(serverId, out var server) || !loadFromDb)
        {
            if (server != null)
                server.Project ??= await GetProject(server.ProjectId);
            return server;
        }

        using var serversLock = await AsyncLock.LockAsync($"Cache_Server_{serverId}");
        server = await GetServer(serverId, false);
        if (server != null)
            return server;

        _logger.LogInformation("Loading old projects and servers...");
        server = await _vhContext.Servers
            .Include(x => x.AccessPoints)
            .Include(x => x.ServerStatuses!.Where(serverStatusEx => serverStatusEx.IsLast))
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ServerId == serverId);

        if (server?.ServerStatuses != null)
            server.ServerStatus = server.ServerStatuses.SingleOrDefault();

        if (server != null)
            server.Project ??= await GetProject(server.ProjectId);

        server = servers.GetOrAdd(serverId, server);
        return server;
    }

    public async Task AddSession(SessionModel session)
    {
        if (session.Access == null)
            throw new ArgumentException($"{nameof(session.Access)} is not initialized.", nameof(session));

        if (session.Device == null)
            throw new ArgumentException($"{nameof(session.Device)} is not initialized.", nameof(session));

        // check session access
        var cachedAccess = await GetAccess(session.AccessId, false);
        if (cachedAccess == null)
            session.Access = (await GetAccesses()).GetOrAdd(session.AccessId, session.Access);

        var sessions = await GetSessions();
        sessions.TryAdd(session.SessionId, session);

        // Manage concurrency. If two sessions add 
        session.Access = await GetAccess(session.AccessId, false);
    }

    public async Task<SessionModel> GetSession(Guid? serverId, long sessionId)
    {
        var sessions = await GetSessions();
        if (!sessions.TryGetValue(sessionId, out var session))
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

        // multiple requests may be in queued so wait for one to finish then check the cache
        using var accessLock = await AsyncLock.LockAsync($"Cache_GetAccessByTokenId_{tokenId}_{deviceId}");
        access = await GetAccessByTokenId(tokenId, deviceId, false);
        if (access != null)
            return access;

        // load from db
        access = await _vhContext.Accesses
            .Include(x => x.AccessToken)
            .Include(x => x.AccessToken!.AccessPointGroup)
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.AccessTokenId == tokenId && x.DeviceId == deviceId);

        // return access
        if (access != null)
            access = accesses.GetOrAdd(access.AccessId, access);
        return access;
    }

    public async Task<AccessModel?> GetAccess(Guid accessId, bool loadFromDb = true)
    {
        var accesses = await GetAccesses();
        if (accesses.TryGetValue(accessId, out var access) || !loadFromDb)
            return access;

        // multiple requests may be in queued so wait for one to finish then check the cache again
        using var accessLock = await AsyncLock.LockAsync($"Cache_GetAccess_{accessId}");
        access = await GetAccess(accessId, false);
        if (access != null)
            return access;

        // load from db
        access = await _vhContext.Accesses
            .Include(x => x.AccessToken)
            .Include(x => x.AccessToken!.AccessPointGroup)
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.AccessId == accessId);


        // return access
        if (access != null)
            access = accesses.GetOrAdd(access.AccessId, access);
        return access;
    }

    public async Task InvalidateProject(Guid projectId)
    {
        // clean project cache
        using (await Mem.ProjectsLock.LockAsync())
        {
            Mem.Projects?.TryRemove(projectId, out _);
        }

        // clear project in server
        var project = await GetProject(projectId);
        using (await Mem.ServersLock.LockAsync())
        {
            if (Mem.Servers != null)
                foreach (var server in Mem.Servers.Values.Where(x => x?.ProjectId == projectId))
                    server!.Project = project;
        }
    }

    public async Task InvalidateProjectServers(Guid projectId)
    {
        var servers = Mem.Servers?.Values
            .Where(server => server != null && server.ProjectId == projectId);

        if (servers == null)
            return;

        foreach (var server in servers)
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
            if (server == null)
                return;

            using (await Mem.ServersLock.LockAsync())
            {
                server.ServerStatus ??= oldServer?.ServerStatus; // keep last status
            }
        }
    }

    public async Task InvalidateSessions()
    {
        using var sessionsLock = await Mem.SessionsLock.LockAsync();
        using var accessesLock = await Mem.AccessesLock.LockAsync();

        Mem.Sessions = null;
        Mem.Accesses = null;
    }


    public async Task UpdateServer(ServerModel server)
    {
        if (server.AccessPoints == null)
            throw new ArgumentException($"{nameof(server.AccessPoints)} can not be null");

        var project = await GetProject(server.ProjectId);

        // restore last status
        using (await Mem.ServersLock.LockAsync())
        {
            server.Project = project;
            Mem.Servers?.AddOrUpdate(server.ServerId, server, (_, oldValue) =>
            {
                server.ServerStatus ??= oldValue?.ServerStatus; //restore last status
                return server;
            });
        }
    }

    public void AddSessionUsage(AccessUsageModel accessUsage)
    {
        if (accessUsage.ReceivedTraffic + accessUsage.SentTraffic == 0)
            return;

        if (!Mem.SessionUsages.TryGetValue(accessUsage.SessionId, out var oldUsage))
        {
            Mem.SessionUsages.TryAdd(accessUsage.SessionId, accessUsage);
        }
        else
        {
            oldUsage.ReceivedTraffic += accessUsage.ReceivedTraffic;
            oldUsage.SentTraffic += accessUsage.SentTraffic;
            oldUsage.LastCycleReceivedTraffic = accessUsage.LastCycleReceivedTraffic;
            oldUsage.LastCycleSentTraffic = accessUsage.LastCycleSentTraffic;
            oldUsage.TotalReceivedTraffic = accessUsage.TotalReceivedTraffic;
            oldUsage.TotalSentTraffic = accessUsage.TotalSentTraffic;
            oldUsage.CreatedTime = accessUsage.CreatedTime;
        }
    }

    private void UpdateTimeoutSessions(
        Dictionary<long, SessionModel> updatedSessions,
        ConcurrentDictionary<long, SessionModel> sessions, DateTime minSessionTime)
    {
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
        var minSessionTime = DateTime.UtcNow - _appOptions.SessionTimeout;
        var sessions = await GetSessions();
        _vhContext.ChangeTracker.Clear();

        // find updated sessions
        var updatedSessions = sessions.Values
            .Where(session => session.LastUsedTime > Mem.LastSavedTime || session.EndTime > Mem.LastSavedTime)
            .ToDictionary(x => x.SessionId);

        // Close Timeout sessions & Archive old sessions
        UpdateTimeoutSessions(updatedSessions, sessions, minSessionTime);

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
            using (await Mem.SessionsLock.LockAsync())
            {
                foreach (var session in sessions.Values.Where(session => session.IsArchived))
                    sessions.TryRemove(session.SessionId, out _);
            }

            // remove unused accesses
            using (await Mem.AccessesLock.LockAsync())
            {
                if (Mem.Accesses != null)
                    foreach (var access in Mem.Accesses.Values.Where(x => x.LastUsedTime < minSessionTime))
                        Mem.Accesses.TryRemove(access.AccessId, out _);
            }
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
        using (await Mem.ServersLock.LockAsync())
        {
            var oldServers = servers
                .Where(x => x.Value == null || x.Value.ServerStatus?.CreatedTime < DateTime.UtcNow - TimeSpan.FromDays(1))
                .ToArray();

            foreach (var oldServer in oldServers)
                servers.TryRemove(oldServer);
        }
    }

    public async Task<SessionModel[]> GetActiveSessions(Guid accessId)
    {
        var sessions = await GetSessions();
        var ret = sessions.Values
            .Where(x => x.EndTime == null && x.AccessId == accessId)
            .OrderBy(x => x.CreatedTime).ToArray();

        return ret;
    }
}
