using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using GrayMint.Authorization.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace VpnHood.AccessServer.Test.Helper;

public class TestAuthorizationProvider : IAuthorizationProvider
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public TestAuthorizationProvider(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task<string?> GetAuthorizationCode(ClaimsPrincipal principal)
    {
        if (principal.FindFirstValue("test_authenticated") == "1")
            return "test_1234";

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var original = scope.ServiceProvider.GetServices<IAuthorizationProvider>();
        return await original.First(x => x != this).GetAuthorizationCode(principal);
    }

    public async Task<Guid?> GetUserId(ClaimsPrincipal principal)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var original = scope.ServiceProvider.GetServices<IAuthorizationProvider>();
        return await original.First(x => x != this).GetUserId(principal);
    }

    public async Task OnAuthenticated(ClaimsPrincipal principal)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var original = scope.ServiceProvider.GetServices<IAuthorizationProvider>();
        await original.First(x => x != this).OnAuthenticated(principal);
    }
}