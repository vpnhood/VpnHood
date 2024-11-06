using System.Collections.Concurrent;
using GrayMint.Common.AspNetCore.ApplicationLifetime;
using GrayMint.Common.AspNetCore.Jobs;
using GrayMint.Common.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Repos;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.Common.Messaging;

namespace VpnHood.AccessServer.Agent.Services;

public class CacheService(
    IOptions<AgentOptions> agentOptions,
    ILogger<CacheService> logger,
    VhAgentRepo vhAgentRepo,
    FarmTokenUpdater farmTokenUpdater,
    CacheRepo cacheRepo)
    : IGrayMintJob, IGrayMintApplicationLifetime
{
    public async Task Init(bool force = true)
    {
        if (!force && !cacheRepo.Projects.IsEmpty)
            return;

        // this will just affect current scope
        var minServerUsedTime = DateTime.UtcNow - TimeSpan.FromHours(1);
        var minSessionUsedTime = DateTime.UtcNow - TimeSpan.FromHours(2);
        var agentInit = await vhAgentRepo.GetInitView(minServerUsedTime, minSessionUsedTime);
        cacheRepo.Projects =
            new ConcurrentDictionary<Guid, ProjectCache>(agentInit.Projects.ToDictionary(x => x.ProjectId));
        cacheRepo.ServerFarms =
            new ConcurrentDictionary<Guid, ServerFarmCache>(agentInit.Farms.ToDictionary(x => x.ServerFarmId));
        cacheRepo.Servers =
            new ConcurrentDictionary<Guid, ServerCache>(agentInit.Servers.ToDictionary(x => x.ServerId));
        cacheRepo.Sessions =
            new ConcurrentDictionary<long, SessionCache>(agentInit.Sessions.ToDictionary(x => x.SessionId));
        cacheRepo.Accesses =
            new ConcurrentDictionary<Guid, AccessCache>(agentInit.Accesses.ToDictionary(x => x.AccessId));
    }

    public async Task InvalidateSessions()
    {
        cacheRepo.LastSavedTime = DateTime.MinValue;
        await SaveChanges();
        await Init();
    }

    public Task<ServerCache[]> GetServers(Guid? projectId = null, Guid? serverFarmId = null)
    {
        var servers = cacheRepo.Servers.Values
            .Where(x => projectId == null || x.ProjectId == projectId)
            .Where(x => serverFarmId == null || x.ServerFarmId == serverFarmId)
            .Select(x => x.UpdateState(agentOptions.Value.LostServerThreshold));
        return Task.FromResult(servers.ToArray());
    }

    public async Task<ProjectCache> GetProject(Guid projectId)
    {
        if (cacheRepo.Projects.TryGetValue(projectId, out var project))
            return project;

        using var projectsLock = await AsyncLock.LockAsync($"Cache_project_{projectId}");
        if (cacheRepo.Projects.TryGetValue(projectId, out project))
            return project;

        project = await vhAgentRepo.ProjectGet(projectId);
        cacheRepo.Projects.TryAdd(projectId, project);
        return project;
    }

    public async Task<ServerCache> GetServer(Guid serverId)
    {
        if (cacheRepo.Servers.TryGetValue(serverId, out var server))
            return server.UpdateState(agentOptions.Value.LostServerThreshold);

        using var serversLock = await AsyncLock.LockAsync($"cache_server_{serverId}");
        if (cacheRepo.Servers.TryGetValue(serverId, out server))
            return server;

        server = await vhAgentRepo.ServerGet(serverId);
        cacheRepo.Servers.TryAdd(serverId, server);
        return server.UpdateState(agentOptions.Value.LostServerThreshold);
    }

    public async Task<ServerFarmCache> GetServerFarm(Guid farmId)
    {
        if (cacheRepo.ServerFarms.TryGetValue(farmId, out var farm))
            return farm;

        using var farmsLock = await AsyncLock.LockAsync($"cache_farm_{farmId}");
        if (cacheRepo.ServerFarms.TryGetValue(farmId, out farm))
            return farm;

        farm = await vhAgentRepo.ServerFarmGet(farmId);
        cacheRepo.ServerFarms.TryAdd(farmId, farm);
        return farm;
    }

    public async Task<AccessCache> GetAccess(Guid accessId)
    {
        if (cacheRepo.Accesses.TryGetValue(accessId, out var access))
            return access;

        // multiple requests may be in queued so wait for one to finish then check the cache again
        using var accessLock = await AsyncLock.LockAsync($"cache_access_{accessId}");
        if (cacheRepo.Accesses.TryGetValue(accessId, out access))
            return access;

        // load from db
        access = await vhAgentRepo.AccessGet(accessId);
        cacheRepo.Accesses.TryAdd(access.AccessId, access);
        return access;
    }


    public Task<SessionCache> GetSession(Guid? serverId, long sessionId)
    {
        if (!cacheRepo.Sessions.TryGetValue(sessionId, out var session)) {
            session = vhAgentRepo.SessionGet(sessionId);
            cacheRepo.Sessions.TryAdd(session.SessionId, session);
        }

        // server validation
        if (serverId != null && session.ServerId != serverId)
            throw new KeyNotFoundException();

        return Task.FromResult(session);
    }


    public async Task<AccessCache> GetAccessByTokenId(AccessTokenModel accessToken, Guid? deviceId)
    {
        var accessTokenId = accessToken.AccessTokenId;

        // get from cache
        var access = cacheRepo.Accesses.Values.FirstOrDefault(x => x.AccessTokenId == accessTokenId && x.DeviceId == deviceId);
        if (access != null)
            return access;

        // multiple requests may be in queued so wait for one to finish then check the cache
        using var accessLock = await AsyncLock.LockAsync($"cache_AccessByTokenId_{accessTokenId}_{deviceId}");
        access = cacheRepo.Accesses.Values.FirstOrDefault(x => x.AccessTokenId == accessTokenId && x.DeviceId == deviceId);
        if (access != null)
            return access;

        // load from db
        access = await vhAgentRepo.AccessFind(accessTokenId, deviceId);
        if (access != null) {
            cacheRepo.Accesses.TryAdd(access.AccessId, access);
            return access;
        }

        // create if not exists
        access = await vhAgentRepo.AccessAdd(accessTokenId, deviceId);
        cacheRepo.Accesses.TryAdd(access.AccessId, access);
        logger.LogInformation(
            "New Access has been created. AccessId: {access.AccessId}, ProjectId: {ProjectId}, ServerFarmId: {ServerFarmId}",
            access.AccessId, accessToken.ProjectId, accessToken.ServerFarmId);

        return access;
    }

    public Task AddSession(SessionCache session)
    {
        cacheRepo.Sessions.TryAdd(session.SessionId, session);
        return Task.CompletedTask;
    }

    public void Ad_AddRewardedAccess(Guid accessId)
    {
        cacheRepo.AdRewardedAccesses.TryAdd(accessId, FastDateTime.Now);
    }

    public bool Ad_IsRewardedAccess(Guid accessId)
    {
        return cacheRepo.AdRewardedAccesses.TryGetValue(accessId, out _);
    }

    public void Ad_AddRewardData(Guid projectId, string adData)
    {
        logger.LogTrace("Ad rewarded. ProjectId: {ProjectId}, AdData: {AdData}", projectId, adData);
        cacheRepo.Ads.TryAdd($"{projectId}/{adData}", FastDateTime.Now);
    }

    public bool Ad_RemoveRewardData(Guid projectId, string adData)
    {
        logger.LogTrace("Ad removed. ProjectId: {ProjectId}, AdData: {AdData}", projectId, adData);
        return cacheRepo.Ads.TryRemove($"{projectId}/{adData}", out _);
    }

    private void CleanupAds()
    {
        var now = FastDateTime.Now;

        // remove old ads
        var oldAdItems = cacheRepo.Ads.Where(x => x.Value < now - agentOptions.Value.AdRewardPendingTimeout).ToArray();
        foreach (var item in oldAdItems)
            cacheRepo.Ads.TryRemove(item);

        // remove old devices
        var oldDeviceItems = cacheRepo.AdRewardedAccesses
            .Where(x => x.Value < now - agentOptions.Value.AdRewardDeviceTimeout).ToArray();
        foreach (var item in oldDeviceItems)
            cacheRepo.AdRewardedAccesses.TryRemove(item);
    }

    public async Task InvalidateServers(Guid? projectId = null, Guid? serverFarmId = null,
        Guid? serverProfileId = null, Guid? serverId = null)
    {
        var serverIds = cacheRepo.Servers.Values
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
        cacheRepo.Projects.TryRemove(projectId, out _);
        return Task.CompletedTask;
    }

    public Task InvalidateServerFarm(Guid serverFarmId)
    {
        cacheRepo.ServerFarms.TryRemove(serverFarmId, out _);

        // invalidate all servers in the farm
        var serverIds = cacheRepo.Servers.Values
            .Where(x => x.ServerFarmId == serverFarmId)
            .Select(x => x.ServerId)
            .ToArray();

        // maybe there is no server in the cache so we need make sure the farm is updated
        return serverIds.Any()
            ? InvalidateServers(serverIds)
            : farmTokenUpdater.UpdateAndSaveChanges([serverFarmId]);
    }

    public Task InvalidateServer(Guid serverId)
    {
        return InvalidateServers([serverId]);
    }

    public Task InvalidateAccessToken(Guid accessTokenId)
    {
        var items = cacheRepo.Accesses.Where(x => x.Value.AccessTokenId == accessTokenId);
        foreach (var item in items)
            cacheRepo.Accesses.TryRemove(item);

        return Task.CompletedTask;
    }

    public async Task InvalidateServers(Guid[] serverIds)
    {
        // invalid servers that already exist in cache
        serverIds = serverIds.Intersect(cacheRepo.Servers.Keys).ToArray();
        var servers = await vhAgentRepo.ServersGet(serverIds);

        // recover server status
        foreach (var serverId in serverIds)
            if (cacheRepo.Servers.TryRemove(serverId, out var oldServer)) {
                var server = servers.FirstOrDefault(x => x.ServerId == serverId);
                if (server != null)
                    server.ServerStatus = oldServer.ServerStatus;
            }

        // set new servers
        foreach (var server in servers)
            cacheRepo.Servers.TryAdd(server.ServerId, server);

        // find involved farms
        var farmIds = servers.Select(x => x.ServerFarmId).Distinct().ToArray();
        await farmTokenUpdater.UpdateAndSaveChanges(farmIds);
    }


    public void AddSessionUsage(AccessUsageModel accessUsage)
    {
        if (accessUsage.ReceivedTraffic + accessUsage.SentTraffic == 0)
            return;

        if (!cacheRepo.SessionUsages.TryGetValue(accessUsage.SessionId, out var oldUsage)) {
            cacheRepo.SessionUsages.TryAdd(accessUsage.SessionId, accessUsage);
        }
        else {
            oldUsage.ReceivedTraffic += accessUsage.ReceivedTraffic;
            oldUsage.SentTraffic += accessUsage.SentTraffic;
            oldUsage.LastCycleReceivedTraffic = accessUsage.LastCycleReceivedTraffic;
            oldUsage.LastCycleSentTraffic = accessUsage.LastCycleSentTraffic;
            oldUsage.TotalReceivedTraffic = accessUsage.TotalReceivedTraffic;
            oldUsage.TotalSentTraffic = accessUsage.TotalSentTraffic;
            oldUsage.CreatedTime = accessUsage.CreatedTime;
            oldUsage.IsAdReward |= accessUsage.IsAdReward;
        }
    }

    private IEnumerable<SessionCache> GetUpdatedSessions()
    {
        foreach (var session in cacheRepo.Sessions.Values) {
            // check is updated from last usage
            var isUpdated = session.LastUsedTime > cacheRepo.LastSavedTime || session.EndTime > cacheRepo.LastSavedTime;

            // check timeout
            var minSessionTime = DateTime.UtcNow - agentOptions.Value.SessionPermanentlyTimeout;
            if (session.EndTime == null && session.LastUsedTime < minSessionTime) {
                if (session.ErrorCode != SessionErrorCode.Ok)
                    logger.LogWarning(
                        "Session has error but it has not been closed. SessionId: {SessionId}", session.SessionId);
                session.EndTime = DateTime.UtcNow;
                session.ErrorCode = SessionErrorCode.SessionClosed;
                session.ErrorMessage = "timeout";
                session.IsArchived = true;
                isUpdated = true;
            }

            // archive the CloseWait sessions; keep closed session shortly in memory to report the session owner
            var minCloseWaitTime = DateTime.UtcNow - agentOptions.Value.SessionTemporaryTimeout;
            if (session.EndTime != null && session.LastUsedTime < minCloseWaitTime && !session.IsArchived) {
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
        foreach (var entry in ex.Entries) {
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
            vhAgentRepo.SessionUpdate(session);

        // update accesses
        var accessIds = updatedSessions.Select(x => x.AccessId).Distinct();
        foreach (var accessId in accessIds) {
            var access = await GetAccess(accessId);
            vhAgentRepo.AccessUpdate(access);
        }

        // save updated sessions
        try {
            logger.LogInformation(
                "Saving Sessions... Projects: {Projects}, Servers: {Servers}, Sessions: {Sessions}, ModifiedSessions: {ModifiedSessions}",
                updatedSessions.DistinctBy(x => x.ProjectId).Count(),
                updatedSessions.DistinctBy(x => x.ServerId).Count(), cacheRepo.Sessions.Count, updatedSessions.Length);

            await vhAgentRepo.SaveChangesAsync();

            // cleanup: remove archived sessions
            foreach (var sessionPair in cacheRepo.Sessions.Where(pair => pair.Value.IsArchived))
                cacheRepo.Sessions.TryRemove(sessionPair);
        }
        catch (DbUpdateConcurrencyException ex) {
            await ResolveDbUpdateConcurrencyException(ex);
            vhAgentRepo.ClearChangeTracker();
            logger.LogError(ex, "Could not flush sessions. I will resolve it for the next try.");
        }
        catch (Exception ex) {
            vhAgentRepo.ClearChangeTracker();
            logger.LogError(ex, "Could not flush sessions.");
        }

        // save access usages
        var sessionUsages = cacheRepo.SessionUsages.Values.ToArray();
        cacheRepo.SessionUsages = new ConcurrentDictionary<long, AccessUsageModel>();
        try {
            await vhAgentRepo.AccessUsageAdd(sessionUsages);
            await vhAgentRepo.SaveChangesAsync();

            // cleanup: remove unused accesses
            var minSessionTime = DateTime.UtcNow - agentOptions.Value.SessionPermanentlyTimeout;
            foreach (var accessPair in cacheRepo.Accesses.Where(pair => pair.Value.LastUsedTime < minSessionTime))
                cacheRepo.Accesses.TryRemove(accessPair);
        }
        catch (Exception ex) {
            vhAgentRepo.ClearChangeTracker();
            logger.LogError(ex, "Could not write AccessUsages.");
        }

        // ServerStatus
        try {
            var serverStatuses = cacheRepo.Servers.Values
                .Where(server => server.ServerStatus?.CreatedTime > cacheRepo.LastSavedTime)
                .Select(server => server.ServerStatus!)
                .ToArray();

            logger.LogInformation("Saving Server Status... Projects: {ProjectCount}, Servers: {ServerCount}",
                serverStatuses.DistinctBy(x => x.ProjectId).Count(), serverStatuses.Length);

            if (serverStatuses.Any())
                await vhAgentRepo.AddAndSaveServerStatuses(serverStatuses);

            //cleanup: remove lost servers
            var minStatusTime = DateTime.UtcNow - agentOptions.Value.SessionPermanentlyTimeout;
            foreach (var serverPair in cacheRepo.Servers.Where(pair =>
                         pair.Value.ServerStatus?.CreatedTime < minStatusTime))
                cacheRepo.Servers.TryRemove(serverPair);
        }
        catch (DbUpdateConcurrencyException ex) {
            await ResolveDbUpdateConcurrencyException(ex);
            vhAgentRepo.ClearChangeTracker();
            logger.LogError(ex, "Could not save servers status. I've resolved it for the next try.");
        }
        catch (Exception ex) {
            vhAgentRepo.ClearChangeTracker();
            logger.LogError(ex, "Could not save servers status.");
        }

        cacheRepo.LastSavedTime = savingTime;
        logger.LogInformation("The cache has been saved.");
    }

    private async Task CloseOldSessions()
    {
        try {
            var minSessionTime = DateTime.UtcNow - agentOptions.Value.SessionPermanentlyTimeout;
            logger.LogInformation("Closing old sessions... MinSessionTime: {MinSessionTime}", minSessionTime);
            var oldSessionCount = await vhAgentRepo.SessionCloseTimeouts(minSessionTime: minSessionTime);
            logger.LogInformation("Old sessions has been closed. OldSessionCount: {OldSessionCount}", oldSessionCount);
        }
        catch (Exception ex) {
            logger.LogError(ex, "Could not close old sessions");
        }
    }

    public Task<SessionCache[]> GetActiveSessions(Guid accessId)
    {
        var activeSessions = cacheRepo.Sessions.Values
            .Where(x => x.EndTime == null && x.AccessId == accessId)
            .OrderBy(x => x.CreatedTime)
            .ToArray();

        return Task.FromResult(activeSessions);
    }

    public async Task RunJob(CancellationToken cancellationToken)
    {
        CleanupAds();
        await SaveChanges();
        await CloseOldSessions();
    }

    public Task ApplicationStarted()
    {
        return Task.CompletedTask;
    }

    public Task ApplicationStopping()
    {
        logger.LogInformation("Application is stopping. Saving cache...");
        return SaveChanges();
    }
}