using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Agent.Repos;

namespace VpnHood.AccessServer.Agent.Controllers;

[ApiController]
[Route("/api/cache")]
[Authorize(AuthenticationSchemes = BotAuthenticationDefaults.AuthenticationScheme, Roles = "System")]
public class CacheController : ControllerBase
{
    private readonly CacheRepo _cacheRepo;

    public CacheController(CacheRepo cacheRepo)
    {
        _cacheRepo = cacheRepo;
    }

    [HttpPost("servers/{serverId:guid}/invalidate")]
    public async Task InvalidateServer(Guid serverId)
    {
        await _cacheRepo.InvalidateServer(serverId);
    }

    [HttpGet("projects/{projectId:guid}/servers")]
    public async Task<Dtos.Server[]> GetServers(Guid projectId)
    {
        Models.Server[] servers = (await _cacheRepo.GetServers())
            .Values
            .Where(x => x != null && x.ProjectId == projectId)
            .ToArray()!;
        return servers.Select(Dtos.Server.FromModel).ToArray();
    }

    [HttpPost("projects/{projectId:guid}/invalidate")]
    public async Task InvalidateProject(Guid projectId)
    {
        await _cacheRepo.InvalidateProject(projectId);
    }

    [HttpGet("sessions/{sessionId:long}")]
    public async Task<Dtos.Session> GetSession(long sessionId)
    {
        var sessionModel = await _cacheRepo.GetSession(sessionId);
        return Dtos.Session.FromModel(sessionModel);
    }

    [HttpPost("sessions/invalidate")]
    public async Task InvalidateSessions()
    {
        await _cacheRepo.SaveChanges(true);
        await _cacheRepo.InvalidateSessions();
    }


    [HttpPost("flush")]
    public async Task Flush()
    {
        await _cacheRepo.SaveChanges(true);
    }
}