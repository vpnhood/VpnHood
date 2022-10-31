using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using GrayMint.Common.AspNetCore;
using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using GrayMint.Common.AspNetCore.Utils;
using VpnHood.AccessServer.Agent.Persistence;
using VpnHood.AccessServer.Agent.Services;

namespace VpnHood.AccessServer.Agent;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("App"));
        builder.AddGrayMintCommonServices(builder.Configuration.GetSection("App"), new RegisterServicesOptions() {AddSwaggerVersioning = false});

        builder.Services.AddAuthentication()
            .AddBotAuthentication(builder.Configuration.GetSection("Auth"), builder.Environment.IsProduction());

        builder.Services.AddDbContextPool<VhContext>(options =>
        {
            options.UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase"));
        });

        builder.Services.AddScoped<SessionService>();
        builder.Services.AddScoped<CacheService>();
        builder.Services.AddScoped<IBotAuthenticationProvider, BotAuthenticationProvider>();

        //---------------------
        // Create App
        //---------------------
        var webApp = builder.Build();
        webApp.UseAppCommonServices(new UseServicesOptions());
        await AppCommon.CheckDatabaseCommand<VhContext>(webApp, args);

        await AppCommon.RunAsync(webApp, args);
    }

    public static string CreateSystemToken(byte[] key, string authorizationCode)
    {
        var claims = new Claim[]
        {
            new("usage_type", "system"),
            new("authorization_code", authorizationCode),
            new(JwtRegisteredClaimNames.Sub, "system"),
            new(JwtRegisteredClaimNames.Email, "system@local"),
        };

        var ret = JwtUtil.CreateSymmetricJwt(key,
            "auth.vpnhood.com",
            "access.vpnhood.com",
            null,
            null,
            claims,
            new[] { "System" });

        return ret;
    }
}