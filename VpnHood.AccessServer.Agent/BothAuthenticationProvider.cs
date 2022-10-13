using System.Security.Claims;
using GrayMint.Common.AspNetCore.Auth.BotAuthentication;

namespace VpnHood.AccessServer.Agent;

public class BothAuthenticationProvider : IBotAuthenticationProvider
{
    public Task<string> GetAuthCode(ClaimsPrincipal principal)
    {
        throw new NotImplementedException();
    }
}