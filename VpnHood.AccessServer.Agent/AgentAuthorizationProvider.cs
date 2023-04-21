using System.Security.Claims;
using GrayMint.Authorization.Abstractions;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Services;

namespace VpnHood.AccessServer.Agent;

public class AgentAuthorizationProvider : IAuthorizationProvider
{
    private readonly CacheService _cacheService;
    private readonly AgentOptions _agentOptions;

    public AgentAuthorizationProvider(CacheService cacheService, IOptions<AgentOptions> agentOptions)
    {
        _cacheService = cacheService;
        _agentOptions = agentOptions.Value;
    }

    public async Task<string?> GetAuthorizationCode(ClaimsPrincipal principal)
    {
        if (principal.HasClaim("usage_type", "agent"))
        {
            var id = await GetUserId(principal) ?? throw new Exception("Could not find server id.");
            var server = await _cacheService.GetServer(id);
            return server.AuthorizationCode.ToString();
        }

        if (principal.HasClaim("usage_type", "system"))
        {
            return _agentOptions.SystemAuthorizationCode;
        }

        throw new Exception("The access token has invalid usage_type.");
    }

    public Task<Guid?> GetUserId(ClaimsPrincipal principal)
    {
        var nameIdentifier = principal.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value;
        if (nameIdentifier!=null && Guid.TryParse(nameIdentifier, out var userId))
            return Task.FromResult((Guid?)userId);

        return Task.FromResult((Guid?)null);
    }

    public Task OnAuthenticated(ClaimsPrincipal principal)
    {
        return Task.CompletedTask;
    }
}