using System.Security.Claims;
using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Services;

namespace VpnHood.AccessServer.Agent;

public class BotAuthenticationProvider : IBotAuthenticationProvider
{
    private readonly CacheService _cacheService;
    private readonly AgentOptions _agentOptions;

    public BotAuthenticationProvider(CacheService cacheService, IOptions<AgentOptions> agentOptions)
    {
        _cacheService = cacheService;
        _agentOptions = agentOptions.Value;
    }

    public async Task<string> GetAuthorizationCode(ClaimsPrincipal principal)
    {
        if (principal.HasClaim("usage_type", "agent"))
        {
            var serverId = Guid.Parse(principal.Claims.Single(x=>x.Type==ClaimTypes.NameIdentifier).Value);
            var server = await _cacheService.GetServer(serverId) ?? throw new Exception("Could not find server.");
            return server.AuthorizationCode.ToString();
        }

        if (principal.HasClaim("usage_type", "system"))
        {
            return _agentOptions.SystemAuthorizationCode;
        }
       
        throw new Exception("The access token has invalid usage_type.");
    }
}