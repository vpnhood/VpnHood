using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Services;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;

namespace VpnHood.AccessServer.Agent.Controllers;

[ApiController]
[Route("/api/cache")]
[Authorize(AuthenticationSchemes = BotAuthenticationDefaults.AuthenticationScheme, Roles = "System")]
public class CacheController : ControllerBase
{
    private readonly CacheService _cacheService;
    private readonly AgentOptions _agentOptions;

    public CacheController(CacheService cacheService, IOptions<AgentOptions> agentOptions)
    {
        _cacheService = cacheService;
        _agentOptions = agentOptions.Value;
    }

    [HttpPost("servers/{serverId:guid}/invalidate")]
    public async Task InvalidateServer(Guid serverId)
    {
        await _cacheService.InvalidateServer(serverId);
    }

    [HttpGet("projects/{projectId:guid}/servers")]
    public async Task<Dtos.Server[]> GetServers(Guid projectId)
    {
        var servers = (await _cacheService.GetServers())
            .Values
            .Where(x => x != null && x.ProjectId == projectId)
            .Select(x=> x!.ToDto(_agentOptions.LostServerThreshold))
            .ToArray();

        foreach (var item in servers)
        {
            //item.ServerStatus.ServerModel = null;
            //item.ServerStatus.Project = null;
        }

        return servers;
    }

    [HttpPost("projects/{projectId:guid}/invalidate")]
    public async Task InvalidateProject(Guid projectId)
    {
        await _cacheService.InvalidateProject(projectId);
    }

    [HttpGet("sessions/{sessionId:long}")]
    public async Task<Session> GetSession(long sessionId)
    {
        var sessionModel = await _cacheService.GetSession(sessionId);
        return Session.FromModel(sessionModel);
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