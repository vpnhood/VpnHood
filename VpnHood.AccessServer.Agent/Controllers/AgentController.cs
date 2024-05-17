using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Agent.Services;
using VpnHood.Common.Messaging;
using VpnHood.Server.Access;
using VpnHood.Server.Access.Configurations;
using VpnHood.Server.Access.Messaging;

namespace VpnHood.AccessServer.Agent.Controllers;

[ApiController]
[Route("/api/agent")]
[Authorize(AgentPolicy.VpnServerPolicy)]
public class AgentController(AgentService agentService) : ControllerBase
{
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
        return  agentService.CreateSession(ServerId, sessionRequestEx);
    }

    [HttpGet("sessions/{sessionId}")]
    public  Task<SessionResponseEx> GetSession(uint sessionId, string hostEndPoint, string? clientIp)
    {
        return  agentService.GetSession(ServerId, sessionId, hostEndPoint, clientIp);
    }

    [HttpPost("sessions/{sessionId}/usage")]
    public Task<SessionResponse> AddSessionUsage(uint sessionId, bool closeSession, Traffic traffic, string? adData)
    {
        return  agentService.AddSessionUsage(ServerId, sessionId, closeSession, traffic, adData);
    }

    [HttpPost("status")]
    public  Task<ServerCommand> UpdateServerStatus(ServerStatus serverStatus)
    {
        return agentService.UpdateServerStatus(ServerId, serverStatus);
    }

    [HttpPost("configure")]
    public  Task<ServerConfig> ConfigureServer(ServerInfo serverInfo)
    {
        return agentService.ConfigureServer(ServerId, serverInfo);
    }
}

