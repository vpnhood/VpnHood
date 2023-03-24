using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using Microsoft.Extensions.DependencyInjection;

namespace VpnHood.AccessServer.Test.Helper;

public class TestBotAuthenticationProvider : IBotAuthenticationProvider
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public TestBotAuthenticationProvider(IServiceScopeFactory serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task<string> GetAuthorizationCode(ClaimsPrincipal principal)
    {
        if (principal.FindFirstValue("test_authenticated") == "1")
            return "test_1234";

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var original = scope.ServiceProvider.GetServices<IBotAuthenticationProvider>();
        return await original.First(x => x != this).GetAuthorizationCode(principal);
    }
}