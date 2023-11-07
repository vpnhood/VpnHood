using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GrayMint.Authorization.Authentications;
using GrayMint.Authorization.Authentications.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace VpnHood.AccessServer.Agent.Controllers;


[ApiController]
[Route("/api/system")]
[Authorize(AgentPolicy.SystemPolicy)]
public class SystemController : ControllerBase
{
    private readonly GrayMintAuthentication _grayMintAuthentication;

    public SystemController(GrayMintAuthentication grayMintAuthentication)
    {
        _grayMintAuthentication = grayMintAuthentication;
    }

    [HttpGet("servers/{serverId}/agent-authorization")]
    public async Task<string> GetAgentAuthorization(Guid serverId)
    {
        var claimsIdentity = new ClaimsIdentity();
        claimsIdentity.AddClaim(new Claim("usage_type", "agent"));
        claimsIdentity.AddClaim(new Claim(JwtRegisteredClaimNames.Sub, serverId.ToString()));
        claimsIdentity.AddClaim(new Claim(JwtRegisteredClaimNames.Email, $"{serverId}@local"));

        var authenticationHeader = await _grayMintAuthentication.CreateAuthenticationHeader(
            new CreateTokenParams { ClaimsIdentity = claimsIdentity });

        return authenticationHeader.ToString();
    }

}