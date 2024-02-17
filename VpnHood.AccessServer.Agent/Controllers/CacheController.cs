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
    [HttpGet("projects/{projectId}/servers")]
    public async Task<ServerCache[]> GetServers(Guid projectId, Guid? serverFarmId = null)
    {
        var servers = (await cacheService.GetServers())
            .Where(x => x.ProjectId == projectId)
            .Where(x => serverFarmId ==null || x.ServerFarmId == serverFarmId)
            .ToArray();

        return servers;
    }

    [HttpPost("projects/{projectId}/invalidate")]
    public Task InvalidateProject(Guid projectId)
    {
        return cacheService.InvalidateProject(projectId);
    }

    [HttpPost("projects/{projectId}/invalidate-servers")]
    public Task InvalidateProjectServers(Guid projectId, Guid? serverFarmId = null, Guid? serverProfileId = null)
    {
        return cacheService.InvalidateProjectServers(projectId: projectId, 
            serverFarmId: serverFarmId, 
            serverProfileId: serverProfileId);
    }

    [HttpGet("servers/{serverId}")]
    public async Task<ServerCache?> GetServer(Guid serverId)
    {
        var server = await cacheService.GetServer(serverId);
        return server;
    }

    [HttpPost("servers/{serverId}/invalidate")]
    public Task InvalidateServer(Guid serverId)
    {
        return cacheService.InvalidateServer(serverId);
    }

    [HttpGet("sessions/{sessionId}")]
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


    [HttpPost("flush")]
    public Task Flush()
    {
        return cacheService.SaveChanges();
    }
}