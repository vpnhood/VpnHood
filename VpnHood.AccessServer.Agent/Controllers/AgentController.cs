using System.Security.Claims;
using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Agent.Services;
using VpnHood.Common.Messaging;
using VpnHood.Server;
using VpnHood.Server.Messaging;

namespace VpnHood.AccessServer.Agent.Controllers;

[ApiController]
[Route("/api/agent")]
[Authorize(AuthenticationSchemes = BotAuthenticationDefaults.AuthenticationScheme)]
public class AgentController : ControllerBase
{
    private readonly AgentService _agentService;

    public AgentController(
        AgentService agentService)
    {
        _agentService = agentService;
    }

    private Guid ServerId
    {
        get
        {
            // find serverId from identity claims
            var subject = User.Claims.First(claim => claim.Type == ClaimTypes.NameIdentifier).Value;
            var serverId = Guid.Parse(subject);
            return serverId;

        }
    }

    [HttpPost("sessions")]
    public  Task<SessionResponseEx> CreateSession(SessionRequestEx sessionRequestEx)
    {
        return  _agentService.CreateSession(ServerId, sessionRequestEx);
    }

    [HttpGet("sessions/{sessionId}")]
    public  Task<SessionResponseEx> GetSession(uint sessionId, string hostEndPoint, string? clientIp)
    {
        return  _agentService.GetSession(ServerId, sessionId, hostEndPoint, clientIp);
    }

    [HttpPost("sessions/{sessionId}/usage")]
    public Task<ResponseBase> AddSessionUsage(uint sessionId, bool closeSession, UsageInfo usageInfo)
    {
        return  _agentService.AddSessionUsage(ServerId, sessionId, closeSession, usageInfo);
    }

    [HttpGet("certificates/{hostEndPoint}")]
    public Task<byte[]> GetCertificate(string hostEndPoint)
    {
        return _agentService.GetCertificate(ServerId, hostEndPoint);
    }

    [HttpPost("status")]
    public  Task<ServerCommand> UpdateServerStatus(ServerStatus serverStatus)
    {
        return _agentService.UpdateServerStatus(ServerId, serverStatus);
    }

    [HttpPost("configure")]
    public  Task<ServerConfig> ConfigureServer(ServerInfo serverInfo)
    {
        return _agentService.ConfigureServer(ServerId, serverInfo);
    }
}

