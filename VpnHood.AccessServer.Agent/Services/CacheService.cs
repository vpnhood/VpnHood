using System.Collections.Concurrent;
using GrayMint.Common.Utils;
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
        public ConcurrentDictionary<Guid, ProjectModel> Projects = new();
        public ConcurrentDictionary<Guid, ServerModel> Servers = new();
        public ConcurrentDictionary<long, SessionModel> Sessions = new();
        public ConcurrentDictionary<Guid, AccessModel> Accesses = new();
        public ConcurrentDictionary<long, AccessUsageModel> SessionUsages = new();
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

    public async Task Init(bool force = true)
    {
        if (!force && Mem.Projects.Count > 0)
            return;

        _logger.LogInformation("Loading the old projects and servers...");
        var minServerUsedTime = DateTime.UtcNow - TimeSpan.FromHours(1);
        var serverStatuses = await _vhContext.ServerStatuses
            .Include(serverStatus => serverStatus.Server)
            .Include(serverStatus => serverStatus.Server!.Project)
            .Include(serverStatus => serverStatus.Server!.AccessPoints)
            .Where(serverStatus => serverStatus.IsLast && serverStatus.CreatedTime > minServerUsedTime)
            .ToArrayAsync();

        // set server status
        foreach (var serverStatus in serverStatuses)
            serverStatus.Server!.ServerStatus = serverStatus;

        var servers = serverStatuses
            .Select(serverStatus => serverStatus.Server!)
            .ToDictionary(server => server.ServerId);

        var projects = serverStatuses
            .Select(serverStatus => serverStatus.Project!)
            .DistinctBy(project => project.ProjectId)
            .ToDictionary(project => project.ProjectId);

        _logger.LogInformation("Loading the old accesses and sessions...");
        var sessions = await _vhContext.Sessions
            .Include(session => session.Device)
            .Include(session => session.Access)
            .Include(session => session.Access!.AccessToken)
            .Include(session => session.Access!.AccessToken!.AccessPointGroup)
            .Where(session => !session.IsArchived)
            .AsNoTracking()
            .ToDictionaryAsync(session => session.SessionId);

        var accesses = sessions.Values
            .Select(session => session.Access!)
            .DistinctBy(access => access.AccessId)
            .ToDictionary(access => access.AccessId);

        Mem.Projects = new ConcurrentDictionary<Guid, ProjectModel>(projects);
        Mem.Servers = new ConcurrentDictionary<Guid, ServerModel>(servers);
        Mem.Sessions = new ConcurrentDictionary<long, SessionModel>(sessions);
        Mem.Accesses = new ConcurrentDictionary<Guid, AccessModel>(accesses);
    }

    public async Task InvalidateSessions()
    {
        Mem.LastSavedTime = DateTime.MinValue;
        await SaveChanges();
        await Init();
    }

    public Task<ConcurrentDictionary<Guid, ServerModel>> GetServers()
    {
        return Task.FromResult(Mem.Servers);
    }

    public async Task<ProjectModel?> GetProject(Guid projectId)
    {
        if (Mem.Projects.TryGetValue(projectId, out var project))
            return project;

        using var projectsLock = await AsyncLock.LockAsync($"Cache_project_{projectId}");
        if (Mem.Projects.TryGetValue(projectId, out project))
            return project;

        project = await _vhContext.Projects
            .AsNoTracking()
            .SingleAsync(x => x.ProjectId == projectId);

        project = Mem.Projects.GetOrAdd(projectId, project);
        return project;
    }

    public async Task<ServerModel> GetServer(Guid serverId)
    {
        if (Mem.Servers.TryGetValue(serverId, out var server) && !server.IsDeleted)
            return server;

        using var serversLock = await AsyncLock.LockAsync($"Cache_Server_{serverId}");
        if (Mem.Servers.TryGetValue(serverId, out server))
            return server;

        server = await _vhContext.Servers
            .Include(x => x.AccessPoints)
            .Include(x => x.ServerStatuses!.Where(serverStatusEx => serverStatusEx.IsLast))
            .AsNoTracking()
            .SingleAsync(x => x.ServerId == serverId && !x.IsDeleted);

        if (server.ServerStatuses != null)
            server.ServerStatus = server.ServerStatuses.SingleOrDefault();

        server.Project = await GetProject(server.ProjectId);
        server = Mem.Servers.GetOrAdd(serverId, server);
        return server;
    }

    public async Task AddSession(SessionModel session)
    {
        if (session.Access == null)
            throw new ArgumentException($"{nameof(session.Access)} is not initialized.", nameof(session));

        if (session.Device == null)
            throw new ArgumentException($"{nameof(session.Device)} is not initialized.", nameof(session));

        // check session access
        var access = await GetAccess(session.AccessId) ?? Mem.Accesses.GetOrAdd(session.AccessId, session.Access);
        session.Access = access;

        Mem.Sessions.TryAdd(session.SessionId, session);
    }

    public Task<SessionModel> GetSession(Guid? serverId, long sessionId)
    {
        if (!Mem.Sessions.TryGetValue(sessionId, out var session))
            throw new KeyNotFoundException();

        // server validation
        if (serverId != null && session.ServerId != serverId)
            throw new KeyNotFoundException();

        return Task.FromResult(session);
    }

    public async Task<AccessModel?> GetAccessByTokenId(Guid tokenId, Guid? deviceId)
    {
        // get from cache
        var access = Mem.Accesses.Values.FirstOrDefault(x => x.AccessTokenId == tokenId && x.DeviceId == deviceId);
        if (access != null)
            return access;

        // multiple requests may be in queued so wait for one to finish then check the cache
        using var accessLock = await AsyncLock.LockAsync($"Cache_GetAccessByTokenId_{tokenId}_{deviceId}");
        access = Mem.Accesses.Values.FirstOrDefault(x => x.AccessTokenId == tokenId && x.DeviceId == deviceId);
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
            access = Mem.Accesses.GetOrAdd(access.AccessId, access);
        return access;
    }

    public async Task<AccessModel?> GetAccess(Guid accessId)
    {
        if (Mem.Accesses.TryGetValue(accessId, out var access))
            return access;

        // multiple requests may be in queued so wait for one to finish then check the cache again
        using var accessLock = await AsyncLock.LockAsync($"Cache_GetAccess_{accessId}");
        if (Mem.Accesses.TryGetValue(accessId, out access))
            return access;

        // load from db
        access = await _vhContext.Accesses
            .Include(accessModel => accessModel.AccessToken)
            .Include(accessModel => accessModel.AccessToken!.AccessPointGroup)
            .AsNoTracking()
            .SingleOrDefaultAsync(accessModel => accessModel.AccessId == accessId);

        // return access
        if (access != null)
            access = Mem.Accesses.GetOrAdd(access.AccessId, access);
        return access;
    }

    public async Task InvalidateProject(Guid projectId)
    {
        // clean project cache
        if (!Mem.Projects.TryRemove(projectId, out _))
            return;

        // set updated project for server
        var project = await GetProject(projectId);
        foreach (var server in Mem.Servers.Values.Where(server => server.ProjectId == projectId))
            server.Project = project;
    }

    public async Task InvalidateProjectServers(Guid projectId)
    {
        foreach (var server in Mem.Servers.Values.Where(server => server.ProjectId == projectId))
        {
            await Task.Delay(100); // don't put pressure on db
            await InvalidateServer(server.ServerId);
        }
    }

    public async Task InvalidateServer(Guid serverId)
    {
        if (!Mem.Servers.TryRemove(serverId, out var oldServer))
            return;

        var server = await GetServer(serverId);
        server.ServerStatus ??= oldServer.ServerStatus; // keep last status
    }

    public async Task UpdateServer(ServerModel server)
    {
        if (server.AccessPoints == null)
            throw new ArgumentException($"{nameof(server.AccessPoints)} can not be null.");

        // restore last status
        server.Project = await GetProject(server.ProjectId);
        Mem.Servers.AddOrUpdate(server.ServerId, server, (_, oldServer) =>
        {
            server.ServerStatus ??= oldServer.ServerStatus; //restore last status
            return server;
        });
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

    private IEnumerable<SessionModel> GetUpdatedSessions()
    {
        foreach (var session in Mem.Sessions.Values.Where(session => !session.IsArchived))
        {
            // check is updated from last usage
            var isUpdated = session.LastUsedTime > Mem.LastSavedTime || session.EndTime > Mem.LastSavedTime;

            // check timeout
            var minSessionTime = DateTime.UtcNow - _appOptions.SessionPermanentlyTimeout;
            if (session.EndTime == null && session.LastUsedTime < minSessionTime)
            {
                if (session.ErrorCode != SessionErrorCode.Ok) _logger.LogWarning(
                    "Session has error but it has not been closed. SessionId: {SessionId}", session.SessionId);
                session.EndTime = DateTime.UtcNow;
                session.ErrorCode = SessionErrorCode.SessionClosed;
                session.ErrorMessage = "timeout";
                session.IsArchived = true;
                isUpdated = true;
            }

            // archive the CloseWait sessions; keep closed session shortly in memory to report the session owner
            var minCloseWaitTime = DateTime.UtcNow - _appOptions.SessionTemporaryTimeout;
            if (session.EndTime != null && session.LastUsedTime < minCloseWaitTime)
            {
                session.IsArchived = true;
                isUpdated = true;
            }


            if (isUpdated)
                yield return session;
        }

    }

    private static readonly AsyncLock SaveChangesLock = new();
    public async Task SaveChanges()
    {
        using var lockAsyncResult = await SaveChangesLock.LockAsync(TimeSpan.Zero);
        if (!lockAsyncResult.Succeeded) return;

        _logger.LogInformation("Saving cache...");
        _vhContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
        await using var transaction = _vhContext.Database.CurrentTransaction == null ? await _vhContext.Database.BeginTransactionAsync() : null;
        var savingTime = DateTime.UtcNow;

        // find updated sessions
        var updatedSessions = GetUpdatedSessions().ToArray();

        // update sessions
        // never update archived session, it may not exists on db any more
        foreach (var session in updatedSessions)
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
        var accesses = updatedSessions
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
        catch (DbUpdateConcurrencyException ex)
        {
            //todo
            var log = "Problems: ";
            foreach (var entry in ex.Entries)
            {
                var proposedValues = entry.CurrentValues;
                var databaseValues = await entry.GetDatabaseValuesAsync();

                foreach (var property in proposedValues.Properties)
                {
                    var proposedValue = proposedValues[property];
                    var databaseValue = databaseValues?[property];

                    if (entry.Entity is SessionModel ss) log += $"SessionId : {ss.SessionId}";
                    if (entry.Entity is AccessModel aa) log += $"AccessId : {aa.AccessId}";
                    log += $"prop: {property.Name}, proposedValue: {proposedValue}, databaseValue: {databaseValue}\r\n";

                    // TODO: decide which value should be written to database
                    // proposedValues[property] = <value to be saved>;
                }

                // Refresh original values to bypass next concurrency check
                if (databaseValues != null)
                    entry.OriginalValues.SetValues(databaseValues);
            }
            _vhContext.ChangeTracker.Clear();
            _logger.LogError(ex, $"Could not flush sessions! All archived sessions in cache has been discarded.\r\n{log}");
        }
        catch (Exception ex)
        {
            _vhContext.ChangeTracker.Clear();
            _logger.LogError(ex, "Could not flush sessions! All archived sessions in cache has been discarded.");
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
            _vhContext.ChangeTracker.Clear();
            _logger.LogError(ex, "Could not write AccessUsages! All access Usage has been discarded.");
        }

        // ServerStatus
        try
        {
            await SaveServersStatus();
        }
        catch (Exception ex)
        {
            _vhContext.ChangeTracker.Clear();
            _logger.LogError(ex, "Could not save servers status!");
        }

        if (transaction != null)
            await transaction.CommitAsync();

        Mem.LastSavedTime = savingTime;
        Cleanup();
        _logger.LogInformation("The cache has been saved.");
    }

    private void Cleanup()
    {
        // remove archived sessions
        foreach (var sessionPair in Mem.Sessions.Where(pair => pair.Value.IsArchived))
            Mem.Sessions.TryRemove(sessionPair);

        // remove unused accesses
        var minSessionTime = DateTime.UtcNow - _appOptions.SessionPermanentlyTimeout;
        foreach (var accessPair in Mem.Accesses.Where(pair => pair.Value.LastUsedTime < minSessionTime))
            Mem.Accesses.TryRemove(accessPair);

        //remove lost servers
        foreach (var serverPair in Mem.Servers.Where(pair => pair.Value.ServerStatus == null || pair.Value.ServerStatus.CreatedTime < minSessionTime))
            Mem.Servers.TryRemove(serverPair);
    }

    private static string ToSqlValue<T>(T? value)
    {
        return value?.ToString() ?? "NULL";
    }

    public async Task SaveServersStatus()
    {
        var serverStatuses = Mem.Servers.Values.Select(server => server.ServerStatus)
            .Where(serverStatus => serverStatus?.CreatedTime > Mem.LastSavedTime)
            .Select(serverStatus => serverStatus!)
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
            TunnelSendSpeed = x.TunnelSendSpeed,
        }).ToArray();

        if (!serverStatuses.Any())
            return;

        //todo
        await using var transaction = _vhContext.Database.CurrentTransaction == null ? await _vhContext.Database.BeginTransactionAsync() : null;

        // remove old is last
        var serverIds = serverStatuses.Select(x => x.ServerId).Distinct();
        await _vhContext.ServerStatuses
            .AsNoTracking()
            .Where(serverStatus => serverIds.Contains(serverStatus.ServerId))
            .ExecuteUpdateAsync(setPropertyCalls =>
                setPropertyCalls.SetProperty(serverStatus => serverStatus.IsLast, serverStatus => false));

        // save new statuses
        var values = serverStatuses.Select(x => "\r\n(" +
            $"{(x.IsLast ? 1 : 0)}, '{x.CreatedTime:yyyy-MM-dd HH:mm:ss.fff}', {ToSqlValue(x.AvailableMemory)}, {ToSqlValue(x.CpuUsage)}, " +
            $"'{x.ServerId}', {(x.IsConfigure ? 1 : 0)}, '{x.ProjectId}', " +
            $"{x.SessionCount}, {x.TcpConnectionCount}, {x.UdpConnectionCount}, " +
            $"{x.ThreadCount}, {x.TunnelReceiveSpeed}, {x.TunnelSendSpeed}" +
            ")");

        var sql =
            $"\r\nINSERT INTO {nameof(_vhContext.ServerStatuses)} (" +
            $"{nameof(ServerStatusModel.IsLast)}, {nameof(ServerStatusModel.CreatedTime)}, {nameof(ServerStatusModel.AvailableMemory)}, {nameof(ServerStatusModel.CpuUsage)}, " +
            $"{nameof(ServerStatusModel.ServerId)}, {nameof(ServerStatusModel.IsConfigure)}, {nameof(ServerStatusModel.ProjectId)}, " +
            $"{nameof(ServerStatusModel.SessionCount)}, {nameof(ServerStatusModel.TcpConnectionCount)},{nameof(ServerStatusModel.UdpConnectionCount)}, " +
            $"{nameof(ServerStatusModel.ThreadCount)}, {nameof(ServerStatusModel.TunnelReceiveSpeed)}, {nameof(ServerStatusModel.TunnelSendSpeed)}" +
            ") " +
            $"VALUES {string.Join(',', values)}";
        
        // AddRange has issue on unique index; we got desperate
        await _vhContext.Database.ExecuteSqlRawAsync(sql);

        if (transaction != null)
            await _vhContext.Database.CommitTransactionAsync();
    }

    public Task<SessionModel[]> GetActiveSessions(Guid accessId)
    {
        var activeSessions = Mem.Sessions.Values
            .Where(x => x.EndTime == null && x.AccessId == accessId)
            .OrderBy(x => x.CreatedTime)
            .ToArray();

        return Task.FromResult(activeSessions);
    }
}
