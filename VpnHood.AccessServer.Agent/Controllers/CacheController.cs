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

    [HttpGet("projects/{projectId:guid}/servers")]
    public async Task<Dtos.Server[]> GetServers(Guid projectId)
    {
        var servers = (await _cacheService.GetServers())
            .Values
            .Where(x => x != null && x.ProjectId == projectId)
            .Select(x => x!.ToDto(
                x!.AccessPointGroup?.AccessPointGroupName,
                x.ServerStatus?.ToDto(),
                _agentOptions.LostServerThreshold))
            .ToArray();

        return servers;
    }

    [HttpPost("projects/{projectId:guid}/invalidate")]
    public async Task InvalidateProject(Guid projectId)
    {
        await _cacheService.InvalidateProject(projectId);
    }

    [HttpPost("projects/{projectId:guid}/invalidate-servers")]
    public async Task InvalidateProjectServers(Guid projectId)
    {
        await _cacheService.InvalidateProject(projectId);
    }

    [HttpGet("servers/{serverId:guid}")]
    public async Task<Dtos.Server?> GetServer(Guid serverId)
    {
        var serverModel = await _cacheService.GetServer(serverId);

        var server = serverModel?.ToDto(
            serverModel.AccessPointGroup?.AccessPointGroupName,
            serverModel.ServerStatus?.ToDto(),
            _agentOptions.LostServerThreshold);

        return server;
    }

    [HttpPost("servers/{serverId:guid}/invalidate")]
    public async Task InvalidateServer(Guid serverId)
    {
        await _cacheService.InvalidateServer(serverId);
    }

    [HttpGet("sessions/{sessionId:long}")]
    public async Task<Session> GetSession(long sessionId)
    {
        var sessionModel = await _cacheService.GetSession(null, sessionId);
        return Session.FromModel(sessionModel);
    }

    [HttpPost("sessions/invalidate")]
    public async Task InvalidateSessions()
    {
        await _cacheService.SaveChanges();
        await _cacheService.InvalidateSessions();
    }


    [HttpPost("flush")]
    public async Task Flush()
    {
        await _cacheService.SaveChanges();
    }
}