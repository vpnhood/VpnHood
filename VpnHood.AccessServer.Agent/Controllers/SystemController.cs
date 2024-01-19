using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using GrayMint.Authorization.Authentications;
using GrayMint.Authorization.Authentications.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace VpnHood.AccessServer.Agent.Controllers;


[ApiController]
[Route("/api/system")]
[Authorize(AgentPolicy.SystemPolicy)]
public class SystemController : ControllerBase
{
    private readonly GrayMintAuthenticationOptions _grayMintAuthenticationOptions;
    private readonly GrayMintAuthentication _grayMintAuthentication;

    public SystemController(
        IOptions<GrayMintAuthenticationOptions> grayMintAuthenticationOptions,
        GrayMintAuthentication grayMintAuthentication)
    {
        _grayMintAuthenticationOptions = grayMintAuthenticationOptions.Value;
        _grayMintAuthentication = grayMintAuthentication;
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
    [HttpPost("api-key")] //make sure secret is not exists in url
    public async Task<ApiKey> GetSystemApiKey([FromForm] string secret)
    {
        if (!Convert.FromBase64String(secret).SequenceEqual((_grayMintAuthenticationOptions.Secret)))
            throw new UnauthorizedAccessException();

        var claimsIdentity = new ClaimsIdentity();
        claimsIdentity.AddClaim(new Claim("usage_type", "system"));
        claimsIdentity.AddClaim(new Claim(JwtRegisteredClaimNames.Sub, "system"));
        claimsIdentity.AddClaim(new Claim(ClaimTypes.Role, "System"));
        var apiKey = await _grayMintAuthentication.CreateApiKey(claimsIdentity, new ApiKeyOptions()
        {
            AccessTokenExpirationTime = DateTime.UtcNow.AddYears(13)
        });
        return apiKey;
    }
}