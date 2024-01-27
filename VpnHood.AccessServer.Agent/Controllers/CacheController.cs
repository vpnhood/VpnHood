using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Services;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;

namespace VpnHood.AccessServer.Agent.Controllers;

[ApiController]
[Route("/api/cache")]
[Authorize(AgentPolicy.SystemPolicy)]
public class CacheController(CacheService cacheService, IOptions<AgentOptions> agentOptions)
    : ControllerBase
{
    private readonly AgentOptions _agentOptions = agentOptions.Value;

    [HttpGet("projects/{projectId}/servers")]
    public async Task<VpnServer[]> GetServers(Guid projectId)
    {
        var servers = (await cacheService.GetServers())
            .Values
            .Where(x => x.ProjectId == projectId)
            .Select(x => x.ToDto(_agentOptions.LostServerThreshold))
            .ToArray();

        return servers;
    }

    [HttpPost("projects/{projectId}/invalidate")]
    public Task InvalidateProject(Guid projectId)
    {
        return cacheService.InvalidateProject(projectId);
    }

    [HttpPost("projects/{projectId}/invalidate-servers")]
    public Task InvalidateProjectServers(Guid projectId, Guid? serverFarmId = null, Guid? serverProfileId = null, Guid? certificateId = null)
    {
        return cacheService.InvalidateProjectServers(projectId: projectId, 
            serverFarmId: serverFarmId, 
            serverProfileId: serverProfileId,
            certificateId: certificateId);
    }

    [HttpGet("servers/{serverId}")]
    public async Task<VpnServer?> GetServer(Guid serverId)
    {
        var serverModel = await cacheService.GetServer(serverId);
        var server = serverModel.ToDto(_agentOptions.LostServerThreshold);
        return server;
    }

    [HttpPost("servers/{serverId}/invalidate")]
    public Task InvalidateServer(Guid serverId)
    {
        return cacheService.InvalidateServer(serverId);
    }

    [HttpGet("sessions/{sessionId}")]
    public async Task<Session> GetSession(long sessionId)
    {
        var session = await cacheService.GetSession(null, sessionId);
        return session.ToDto();
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