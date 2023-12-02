using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GrayMint.Authorization.Authentications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer.Agent.Controllers;


[ApiController]
[Route("/api/system")]
[Authorize(AgentPolicy.SystemPolicy)]
public class SystemController : ControllerBase
{
    private readonly GrayMintAuthenticationOptions _grayMintAuthenticationOptions;
    private readonly GrayMintAuthentication _grayMintAuthentication;
    private readonly VhContext _vhContext;

    public SystemController(
        GrayMintAuthenticationOptions grayMintAuthenticationOptions,
        GrayMintAuthentication grayMintAuthentication, 
        VhContext vhContext)
    {
        _grayMintAuthenticationOptions = grayMintAuthenticationOptions;
        _grayMintAuthentication = grayMintAuthentication;
        _vhContext = vhContext;
    }

    [HttpGet("servers/{serverId}/agent-authorization")]
    public async Task<string> GetAgentAuthorization(Guid serverId)
    {
        var claimsIdentity = new ClaimsIdentity();
        claimsIdentity.AddClaim(new Claim("usage_type", "agent"));
        claimsIdentity.AddClaim(new Claim(JwtRegisteredClaimNames.Sub, serverId.ToString()));
        var authenticationHeader = await _grayMintAuthentication.CreateAuthenticationHeader(claimsIdentity, 
            expirationTime: DateTime.UtcNow.AddYears(13));

        return authenticationHeader.ToString();
    }

    [AllowAnonymous]
    [HttpGet("authorization")]
    public async Task<string> GetSystemAuthorization(string secret)
    {
        if (!Convert.FromBase64String(secret).SequenceEqual((_grayMintAuthenticationOptions.Secret)))
            throw new UnauthorizedAccessException();

        var claimsIdentity = new ClaimsIdentity();
        claimsIdentity.AddClaim(new Claim("usage_type", "system"));
        claimsIdentity.AddClaim(new Claim(JwtRegisteredClaimNames.Sub, "system"));
        var authenticationHeader = await _grayMintAuthentication.CreateAuthenticationHeader(claimsIdentity,
            expirationTime: DateTime.UtcNow.AddYears(13));

        return authenticationHeader.ToString();
    }

    [AllowAnonymous]
    [HttpGet("/api/agent/server-token")]
    public async Task<string> GetAgentAuthorization(string managementSecret)
    {
        var base64BinaryData = Convert.FromHexString(managementSecret.Replace("0x", ""));
        var serverId = (await _vhContext.Servers.FirstAsync(x=>x.ManagementSecret.SequenceEqual(base64BinaryData))).ServerId;

        var claimsIdentity = new ClaimsIdentity();
        claimsIdentity.AddClaim(new Claim("usage_type", "agent"));
        claimsIdentity.AddClaim(new Claim(JwtRegisteredClaimNames.Sub, serverId.ToString()));
        var authenticationHeader = await _grayMintAuthentication.CreateAuthenticationHeader(claimsIdentity,
            expirationTime: DateTime.UtcNow.AddYears(13));

        return authenticationHeader.ToString();
    }
}