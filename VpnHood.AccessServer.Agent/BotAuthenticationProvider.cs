using System.Security.Claims;
using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Repos;

namespace VpnHood.AccessServer.Agent;

public class BotAuthenticationProvider : IBotAuthenticationProvider
{
    private readonly CacheRepo _cacheRepo;
    private readonly AgentOptions _agentOptions;

    public BotAuthenticationProvider(CacheRepo cacheRepo, IOptions<AgentOptions> agentOptions)
    {
        _cacheRepo = cacheRepo;
        _agentOptions = agentOptions.Value;
    }

    public async Task<string> GetAuthCode(ClaimsPrincipal principal)
    {
        if (principal.HasClaim("usage_type", "agent"))
        {
            var serverId = Guid.Parse(principal.Claims.Single(x=>x.Type==ClaimTypes.NameIdentifier).Value);
            var server = await _cacheRepo.GetServer(serverId) ?? throw new Exception("Could not find server.");
            return server.AuthorizationCode.ToString();
        }

        if (principal.HasClaim("usage_type", "system"))
        {
            return _agentOptions.SystemAuthorizationCode;
        }
       
        throw new Exception("The access token has invalid usage_type.");
    }
}