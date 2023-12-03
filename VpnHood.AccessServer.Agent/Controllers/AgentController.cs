using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GrayMint.Authorization.Authentications;
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
public class AgentController : ControllerBase
{
    private readonly AgentService _agentService;
    private readonly GrayMintAuthentication _grayMintAuthentication;

    public AgentController(
        AgentService agentService,
        GrayMintAuthentication grayMintAuthentication
        )
    {
        _agentService = agentService;
        _grayMintAuthentication = grayMintAuthentication;
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
    public Task<SessionResponseBase> AddSessionUsage(uint sessionId, bool closeSession, Traffic traffic)
    {
        return  _agentService.AddSessionUsage(ServerId, sessionId, closeSession, traffic);
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

    //todo: temporary for version 442 to upgrade old key
    [HttpGet("authorization")]
    public async Task<string> GetAuthorization()
    {
        var claimsIdentity = new ClaimsIdentity();
        claimsIdentity.AddClaim(new Claim("usage_type", "agent"));
        claimsIdentity.AddClaim(new Claim(JwtRegisteredClaimNames.Sub, ServerId.ToString()));
        var authenticationHeader = await _grayMintAuthentication.CreateAuthenticationHeader(claimsIdentity,
            expirationTime: DateTime.UtcNow.AddYears(13));

        return authenticationHeader.ToString();
    }
}

