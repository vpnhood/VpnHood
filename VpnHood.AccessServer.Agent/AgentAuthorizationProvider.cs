using System.Security.Claims;
using GrayMint.Authorization.Abstractions;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Services;

namespace VpnHood.AccessServer.Agent;

public class AgentAuthorizationProvider(
    CacheService cacheService,
    IOptions<AgentOptions> agentOptions) : IAuthorizationProvider
{
    public async Task<string?> GetAuthorizationCode(ClaimsPrincipal principal)
    {
        if (principal.HasClaim("usage_type", "agent")) {
            var id = await GetUserId(principal) ?? throw new Exception("Could not find server id.");
            var server = await cacheService.GetServer(Guid.Parse(id));
            return server.AuthorizationCode.ToString();
        }

        if (principal.HasClaim("usage_type", "system")) {
            return agentOptions.Value.SystemAuthorizationCode;
        }

        throw new Exception("The access token has invalid usage_type.");
    }

    public Task<string?> GetUserId(ClaimsPrincipal principal)
    {
        var nameIdentifier = principal.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value;
        return Task.FromResult(nameIdentifier ?? null);
    }

    public Task OnAuthenticated(ClaimsPrincipal principal)
    {
        return Task.CompletedTask;
    }

    public Task RestAuthorizationCode(ClaimsPrincipal principal)
    {
        throw new NotSupportedException();
    }
}