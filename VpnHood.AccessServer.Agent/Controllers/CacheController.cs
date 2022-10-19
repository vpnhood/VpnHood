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
    public Task InvalidateServer(Guid serverId)
    {
        _cacheRepo.InvalidateServer(serverId);
        return Task.CompletedTask;
    }

    [HttpGet("servers")]
    public async Task<Dtos.Server[]> GetServers(Guid? projectId = null)
    {
        Models.Server[] servers = (await _cacheRepo.GetServers())
            .Values
            .Where(x => x != null && (projectId == null || x.ProjectId == projectId))
            .ToArray()!;
        return servers.Select(Dtos.Server.FromModel).ToArray();
    }

    [HttpPost("projects/{projectId:guid}/invalidate")]
    public Task InvalidateProject(Guid projectId)
    {
        _cacheRepo.InvalidateServer(projectId);
        return Task.CompletedTask;
    }

    [HttpGet("sessions/{sessionId:long}")]
    public async Task<Dtos.Session> GetSession(long sessionId)
    {
        var sessionModel = await _cacheRepo.GetSession(sessionId);
        return Dtos.Session.FromModel(sessionModel);
    }

    [HttpPost("flush")]
    public async Task Flush()
    {
        await _cacheRepo.SaveChanges();
    }
}