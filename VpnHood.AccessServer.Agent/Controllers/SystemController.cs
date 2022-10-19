﻿using System.IdentityModel.Tokens.Jwt;
using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Agent.Persistence;

namespace VpnHood.AccessServer.Agent.Controllers;

[ApiController]
[Route("/api/system")]
[Authorize(AuthenticationSchemes = BotAuthenticationDefaults.AuthenticationScheme, Roles = "System")]
public class SystemController : ControllerBase
{
    private readonly VhContext _vhContext;
    private readonly BotAuthenticationTokenBuilder _botAuthenticationTokenBuilder;

    public SystemController(VhContext vhContext, BotAuthenticationTokenBuilder botAuthenticationTokenBuilder)
    {
        _vhContext = vhContext;
        _botAuthenticationTokenBuilder = botAuthenticationTokenBuilder;
    }

    [HttpGet("agent-authorization")]
    public async Task<string> GetAgentAuthorization(Guid serverId)
    {
        var claimsIdentity = new ClaimsIdentity();
        claimsIdentity.AddClaim(new Claim("usage_type", "agent"));
        claimsIdentity.AddClaim(new Claim(JwtRegisteredClaimNames.Sub, serverId.ToString()));
        claimsIdentity.AddClaim(new Claim(JwtRegisteredClaimNames.Email, $"{serverId}@local"));

        var authenticationHeader = await _botAuthenticationTokenBuilder.CreateAuthenticationHeader(claimsIdentity);
        return authenticationHeader.ToString();
    }
}