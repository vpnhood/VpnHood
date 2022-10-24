using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Agent.Services;

namespace VpnHood.AccessServer.Agent.Controllers;

[ApiController]
[Route("/api/cache")]
[Authorize(AuthenticationSchemes = BotAuthenticationDefaults.AuthenticationScheme, Roles = "System")]
public class CacheController : ControllerBase
{
    private readonly CacheService _cacheService;

    public CacheController(CacheService cacheService)
    {
        _cacheService = cacheService;
    }

    [HttpPost("servers/{serverId:guid}/invalidate")]
    public async Task InvalidateServer(Guid serverId)
    {
        await _cacheService.InvalidateServer(serverId);
    }

    [HttpGet("projects/{projectId:guid}/servers")]
    public async Task<Dtos.Server[]> GetServers(Guid projectId)
    {
        Models.Server[] servers = (await _cacheService.GetServers())
            .Values
            .Where(x => x != null && x.ProjectId == projectId)
            .ToArray()!;
        return servers.Select(Dtos.Server.FromModel).ToArray();
    }

    [HttpPost("projects/{projectId:guid}/invalidate")]
    public async Task InvalidateProject(Guid projectId)
    {
        await _cacheService.InvalidateProject(projectId);
    }

    [HttpGet("sessions/{sessionId:long}")]
    public async Task<Dtos.Session> GetSession(long sessionId)
    {
        var sessionModel = await _cacheService.GetSession(sessionId);
        return Dtos.Session.FromModel(sessionModel);
    }

    [HttpPost("sessions/invalidate")]
    public async Task InvalidateSessions()
    {
        await _cacheService.SaveChanges(true);
        await _cacheService.InvalidateSessions();
    }


    [HttpPost("flush")]
    public async Task Flush()
    {
        await _cacheService.SaveChanges(true);
    }
}