using System.Security.Claims;
using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using VpnHood.AccessServer.Agent.Repos;

namespace VpnHood.AccessServer.Agent;

public class BotAuthenticationProvider : IBotAuthenticationProvider
{
    private readonly CacheRepo _cacheRepo;
    public BotAuthenticationProvider(CacheRepo cacheRepo)
    {
        _cacheRepo = cacheRepo;
    }

    public async Task<string> GetAuthCode(ClaimsPrincipal principal)
    {
        if (!principal.HasClaim("usage_type", "agent"))
            throw new Exception("The access token has invalid usage_type.");

        var serverId = Guid.Empty;
        var server = await  _cacheRepo.GetServer(serverId) ?? throw new Exception("Could not find server.");
        return server.AuthorizationCode.ToString();
    }
}