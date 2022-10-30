using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer;

public class BotAuthenticationProvider : IBotAuthenticationProvider
{
    private readonly VhContext _vhContext;

    public BotAuthenticationProvider(VhContext vhContext)
    {
        _vhContext = vhContext;
    }

    public async Task<string> GetAuthorizationCode(ClaimsPrincipal principal)
    {
        var tokenEmail = principal.Claims.First(x => x.Type == ClaimTypes.Email).Value;
        var authCode = principal.Claims.FirstOrDefault(x => x.Type == "test_usage")?.Value;
        if (authCode == "test") 
            return authCode;

        var user = await _vhContext.Users.SingleAsync(x=>x.Email == tokenEmail);
        return user.AuthCode ?? throw new Exception($"{nameof(user.AuthCode)} is not set.");
    }
}