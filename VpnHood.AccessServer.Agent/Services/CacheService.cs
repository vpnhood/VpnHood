using System.Collections.Concurrent;
using GrayMint.Common.AspNetCore.Jobs;
using GrayMint.Common.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Agent.Services;

public class CacheService(
    IOptions<AgentOptions> appOptions,
    ILogger<CacheService> logger,
    VhAgentRepo vhAgentRepo)
    : IGrayMintJob
{
    private class MemCache
    {
        public ConcurrentDictionary<Guid, ProjectCache> Projects = new();
        public ConcurrentDictionary<Guid, ServerFarmCache> ServerFarms = new();
        public ConcurrentDictionary<Guid, ServerCache> Servers = new();
        public ConcurrentDictionary<long, SessionCache> Sessions = new();
        public ConcurrentDictionary<Guid, AccessCache> Accesses = new();
        public ConcurrentDictionary<long, AccessUsageModel> SessionUsages = new();
        public DateTime LastSavedTime = DateTime.MinValue;
    }

    private static MemCache Mem { get; } = new();
    private readonly AgentOptions _appOptions = appOptions.Value;

    public async Task Init(bool force = true)
    {
        if (!force && !Mem.Projects.IsEmpty)
            return;

        // this will just affect current scope
        var minServerUsedTime = DateTime.UtcNow - TimeSpan.FromHours(1);
        var agentInit = await vhAgentRepo.GetInitView(minServerUsedTime);
        Mem.Projects = new ConcurrentDictionary<Guid, ProjectCache>(agentInit.Projects.ToDictionary(x => x.ProjectId));
        Mem.ServerFarms = new ConcurrentDictionary<Guid, ServerFarmCache>(agentInit.Farms.ToDictionary(x => x.ServerFarmId));
        Mem.Servers = new ConcurrentDictionary<Guid, ServerCache>(agentInit.Servers.ToDictionary(x => x.ServerId));
        Mem.Sessions = new ConcurrentDictionary<long, SessionCache>(agentInit.Sessions.ToDictionary(x => x.SessionId));
        Mem.Accesses = new ConcurrentDictionary<Guid, AccessCache>(agentInit.Accesses.ToDictionary(x => x.AccessId));
    }

    public async Task InvalidateSessions()
    {
        Mem.LastSavedTime = DateTime.MinValue;
        await SaveChanges();
        await Init();
    }

    public Task<ServerCache[]> GetServers()
    {
        var servers = Mem.Servers.Values
            .Select(x => x.UpdateState(appOptions.Value.LostServerThreshold));
        return Task.FromResult(servers.ToArray());
    }

    public async Task<ProjectCache> GetProject(Guid projectId)
    {
        if (Mem.Projects.TryGetValue(projectId, out var project))
            return project;

        using var projectsLock = await AsyncLock.LockAsync($"Cache_project_{projectId}");
        if (Mem.Projects.TryGetValue(projectId, out project))
            return project;

        project = await vhAgentRepo.GetProject(projectId);
        Mem.Projects.TryAdd(projectId, project);
        return project;
    }

    public async Task<ServerCache> GetServer(Guid serverId)
    {
        if (Mem.Servers.TryGetValue(serverId, out var server))
            return server.UpdateState(appOptions.Value.LostServerThreshold);

        using var serversLock = await AsyncLock.LockAsync($"cache_server_{serverId}");
        if (Mem.Servers.TryGetValue(serverId, out server))
            return server;

        server = await vhAgentRepo.GetServer(serverId);
        Mem.Servers.TryAdd(serverId, server);
        return server.UpdateState(appOptions.Value.LostServerThreshold);
    }

    public async Task<ServerFarmCache> GetFarm(Guid farmId)
    {
        if (Mem.ServerFarms.TryGetValue(farmId, out var farm))
            return farm;

        using var farmsLock = await AsyncLock.LockAsync($"cache_farm_{farmId}");
        if (Mem.ServerFarms.TryGetValue(farmId, out farm))
            return farm;

        farm = await vhAgentRepo.GetFarm(farmId);
        Mem.ServerFarms.TryAdd(farmId, farm);
        return farm;
    }

    public async Task<AccessCache> GetAccess(Guid accessId)
    {
        if (Mem.Accesses.TryGetValue(accessId, out var access))
            return access;

        // multiple requests may be in queued so wait for one to finish then check the cache again
        using var accessLock = await AsyncLock.LockAsync($"cache_access_{accessId}");
        if (Mem.Accesses.TryGetValue(accessId, out access))
            return access;

        // load from db
        access = await vhAgentRepo.GetAccess(accessId);
        Mem.Accesses.TryAdd(access.AccessId, access);
        return access;
    }


    public Task<SessionCache> GetSession(Guid? serverId, long sessionId)
    {
        if (!Mem.Sessions.TryGetValue(sessionId, out var session))
            throw new KeyNotFoundException();

        // server validation
        if (serverId != null && session.ServerId != serverId)
            throw new KeyNotFoundException();

        return Task.FromResult(session);
    }


    public async Task<AccessCache?> GetAccessByTokenId(Guid accessTokenId, Guid? deviceId)
    {
        // get from cache
        var access = Mem.Accesses.Values.FirstOrDefault(x => x.AccessTokenId == accessTokenId && x.DeviceId == deviceId);
        if (access != null)
            return access;

        // multiple requests may be in queued so wait for one to finish then check the cache
        using var accessLock = await AsyncLock.LockAsync($"cache_AccessByTokenId_{accessTokenId}_{deviceId}");
        access = Mem.Accesses.Values.FirstOrDefault(x => x.AccessTokenId == accessTokenId && x.DeviceId == deviceId);
        if (access != null)
            return access;

        // load from db
        access = await vhAgentRepo.GetAccessOrDefault(accessTokenId, deviceId);
        if (access != null)
            Mem.Accesses.TryAdd(access.AccessId, access);

        return access;
    }

    public Task AddSession(SessionCache session)
    {
        Mem.Sessions.TryAdd(session.SessionId, session);
        return Task.CompletedTask;
    }

    public async Task InvalidateServers(Guid? projectId = null, Guid? serverFarmId = null,
        Guid? serverProfileId = null, Guid? serverId = null)
    {
        var serverIds = Mem.Servers.Values
            .Where(server => server.ProjectId == projectId || projectId == null)
            .Where(server => server.ServerFarmId == serverFarmId || serverFarmId == null)
            .Where(server => server.ServerProfileId == serverProfileId || serverProfileId == null)
            .Where(server => server.ServerId == serverId || serverId == null)
            .Select(x => x.ServerId)
            .ToArray();

        await InvalidateServers(serverIds);
    }

    public Task InvalidateProject(Guid projectId)
    {
        Mem.Projects.TryRemove(projectId, out _);
        return Task.CompletedTask;
    }

    public Task InvalidateServerFarm(Guid serverFarmId)
    {
        Mem.ServerFarms.TryRemove(serverFarmId, out _);
        var serverIds = Mem.Servers.Values
            .Where(x => x.ServerFarmId == serverFarmId)
            .Select(x => x.ServerId)
            .ToArray();

        return InvalidateServers(serverIds);
    }

    public Task InvalidateServer(Guid serverId)
    {
        return InvalidateServers([serverId]);
    }

    public async Task InvalidateServers(Guid[] serverIds)
    {
        // invalid servers that already exist in cache
        serverIds = serverIds.Intersect(Mem.Servers.Keys).ToArray();
        var servers = await vhAgentRepo.GetServers(serverIds);

        // recover server status
        foreach (var serverId in serverIds)
            if (Mem.Servers.TryRemove(serverId, out var oldServer))
            {
                var server = servers.FirstOrDefault(x => x.ServerId == serverId);
                if (server != null)
                    server.ServerStatus = oldServer.ServerStatus;
            }

        // set new servers
        foreach (var server in servers)
            Mem.Servers.TryAdd(server.ServerId, server);
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

    private IEnumerable<SessionCache> GetUpdatedSessions()
    {
        foreach (var session in Mem.Sessions.Values)
        {
            // check is updated from last usage
            var isUpdated = session.LastUsedTime > Mem.LastSavedTime || session.EndTime > Mem.LastSavedTime;

            // check timeout
            var minSessionTime = DateTime.UtcNow - _appOptions.SessionPermanentlyTimeout;
            if (session.EndTime == null && session.LastUsedTime < minSessionTime)
            {
                if (session.ErrorCode != SessionErrorCode.Ok) logger.LogWarning(
                    "Session has error but it has not been closed. SessionId: {SessionId}", session.SessionId);
                session.EndTime = DateTime.UtcNow;
                session.ErrorCode = SessionErrorCode.SessionClosed;
                session.ErrorMessage = "timeout";
                session.IsArchived = true;
                isUpdated = true;
            }

            // archive the CloseWait sessions; keep closed session shortly in memory to report the session owner
            var minCloseWaitTime = DateTime.UtcNow - _appOptions.SessionTemporaryTimeout;
            if (session.EndTime != null && session.LastUsedTime < minCloseWaitTime && !session.IsArchived)
            {
                session.IsArchived = true;
                isUpdated = true;
            }


            // already archived is marked as updated and must be saved
            if (isUpdated || session.IsArchived)
                yield return session;
        }

    }

    private static async Task ResolveDbUpdateConcurrencyException(DbUpdateConcurrencyException ex)
    {
        foreach (var entry in ex.Entries)
        {
            //var proposedValues = entry.CurrentValues;
            var databaseValues = await entry.GetDatabaseValuesAsync();

            if (entry.State == EntityState.Deleted)
                entry.State = EntityState.Detached;
            else if (databaseValues != null)
                entry.OriginalValues.SetValues(databaseValues);
        }
    }

    private static readonly AsyncLock SaveChangesLock = new();
    public async Task SaveChanges()
    {
        using var lockAsyncResult = await SaveChangesLock.LockAsync(TimeSpan.Zero);
        if (!lockAsyncResult.Succeeded) return;

        logger.LogTrace("Saving cache...");
        vhAgentRepo.SetCommandTimeout(TimeSpan.FromMinutes(5));
        var savingTime = DateTime.UtcNow;

        // find updated sessions
        var updatedSessions = GetUpdatedSessions().ToArray();

        // update sessions
        // never update archived session, it may not exist on db anymore
        foreach (var session in updatedSessions)
            vhAgentRepo.UpdateSession(session);

        // update accesses
        var accessIds = updatedSessions.Select(x => x.AccessId).Distinct();
        foreach (var accessId in accessIds)
        {
            var access = await GetAccess(accessId);
            vhAgentRepo.UpdateAccess(access);
        }

        // save updated sessions
        try
        {
            logger.LogInformation("Saving Sessions... Projects: {Projects}, Servers: {Servers}, Sessions: {Sessions}, ModifiedSessions: {ModifiedSessions}",
                updatedSessions.DistinctBy(x => x.ProjectId).Count(), updatedSessions.DistinctBy(x => x.ServerId).Count(), Mem.Sessions.Count, updatedSessions.Length);

            await vhAgentRepo.SaveChangesAsync();

            // cleanup: remove archived sessions
            foreach (var sessionPair in Mem.Sessions.Where(pair => pair.Value.IsArchived))
                Mem.Sessions.TryRemove(sessionPair);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await ResolveDbUpdateConcurrencyException(ex);
            vhAgentRepo.ClearChangeTracker();
            logger.LogError(ex, "Could not flush sessions. I will resolve it for the next try.");
        }
        catch (Exception ex)
        {
            vhAgentRepo.ClearChangeTracker();
            logger.LogError(ex, "Could not flush sessions.");
        }

        // save access usages
        var sessionUsages = Mem.SessionUsages.Values.ToArray();
        Mem.SessionUsages = new ConcurrentDictionary<long, AccessUsageModel>();
        try
        {
            await vhAgentRepo.AddAccessUsages(sessionUsages);
            await vhAgentRepo.SaveChangesAsync();

            // cleanup: remove unused accesses
            var minSessionTime = DateTime.UtcNow - _appOptions.SessionPermanentlyTimeout;
            foreach (var accessPair in Mem.Accesses.Where(pair => pair.Value.LastUsedTime < minSessionTime))
                Mem.Accesses.TryRemove(accessPair);
        }
        catch (Exception ex)
        {
            vhAgentRepo.ClearChangeTracker();
            logger.LogError(ex, "Could not write AccessUsages.");
        }

        // ServerStatus
        try
        {
            var serverStatuses = Mem.Servers.Values
                .Where(server => server.ServerStatus?.CreatedTime > Mem.LastSavedTime)
                .Select(server => server.ServerStatus!)
                .ToArray();

            logger.LogInformation("Saving Server Status... Projects: {ProjectCount}, Servers: {ServerCount}",
                serverStatuses.DistinctBy(x => x.ProjectId).Count(), serverStatuses.Length);

            if (serverStatuses.Any())
                await vhAgentRepo.AddAndSaveServerStatuses(serverStatuses);

            //cleanup: remove lost servers
            var minStatusTime = DateTime.UtcNow - _appOptions.SessionPermanentlyTimeout;
            foreach (var serverPair in Mem.Servers.Where(pair => pair.Value.ServerStatus?.CreatedTime < minStatusTime))
                Mem.Servers.TryRemove(serverPair);

        }
        catch (DbUpdateConcurrencyException ex)
        {
            await ResolveDbUpdateConcurrencyException(ex);
            vhAgentRepo.ClearChangeTracker();
            logger.LogError(ex, "Could not save servers status. I've resolved it for the next try.");
        }
        catch (Exception ex)
        {
            vhAgentRepo.ClearChangeTracker();
            logger.LogError(ex, "Could not save servers status.");
        }

        Mem.LastSavedTime = savingTime;
        logger.LogTrace("The cache has been saved.");
    }

    public Task<SessionCache[]> GetActiveSessions(Guid accessId)
    {
        var activeSessions = Mem.Sessions.Values
            .Where(x => x.EndTime == null && x.AccessId == accessId)
            .OrderBy(x => x.CreatedTime)
            .ToArray();

        return Task.FromResult(activeSessions);
    }

    public Task RunJob(CancellationToken cancellationToken)
    {
        return SaveChanges();
    }
}
