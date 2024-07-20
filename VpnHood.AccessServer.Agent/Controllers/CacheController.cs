using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Agent.Services;
using VpnHood.AccessServer.Persistence.Caches;

namespace VpnHood.AccessServer.Agent.Controllers;

[ApiController]
[Route("/api/cache")]
[Authorize(AgentPolicy.SystemPolicy)]
public class CacheController(CacheService cacheService)
    : ControllerBase
{
    [HttpPost("projects/{projectId:guid}/invalidate")]
    public Task InvalidateProject(Guid projectId)
    {
        return cacheService.InvalidateProject(projectId);
    }

    [HttpGet("servers")]
    public async Task<ServerCache[]> GetServers(Guid? projectId = null, Guid? serverFarmId = null)
    {
        var servers = (await cacheService.GetServers(projectId: projectId, serverFarmId: serverFarmId))
            .Where(x => x.ProjectId == projectId || projectId == null)
            .Where(x => serverFarmId == null || x.ServerFarmId == serverFarmId)
            .ToArray();

        return servers;
    }

    [HttpGet("servers/{serverId:guid}")]
    public async Task<ServerCache?> GetServer(Guid serverId)
    {
        var server = await cacheService.GetServer(serverId);
        return server;
    }

    [HttpPost("servers/invalidate")]
    public Task InvalidateServers(Guid projectId, Guid? serverFarmId = null, Guid? serverProfileId = null,
        Guid? serverId = null)
    {
        return cacheService.InvalidateServers(projectId: projectId, serverFarmId: serverFarmId,
            serverProfileId: serverProfileId, serverId: serverId);
    }

    [HttpPost("server-farms/{serverFarmId:guid}/invalidate")]
    public Task InvalidateServerFarm(Guid serverFarmId)
    {
        return cacheService.InvalidateServerFarm(serverFarmId);
    }

    [HttpGet("sessions/{sessionId:long}")]
    public async Task<SessionCache> GetSession(long sessionId)
    {
        var session = await cacheService.GetSession(null, sessionId);
        return session;
    }

    [HttpPost("sessions/invalidate")]
    public async Task InvalidateSessions()
    {
        await cacheService.SaveChanges();
        await cacheService.InvalidateSessions();
    }

    [HttpPost("access-tokens/{accessTokenId}/invalidate")]
    public async Task InvalidateSessions(Guid accessTokenId)
    {
        await cacheService.InvalidateAccessToken(accessTokenId);
    }


    [HttpPost("flush")]
    public Task Flush()
    {
        return cacheService.SaveChanges();
    }
}