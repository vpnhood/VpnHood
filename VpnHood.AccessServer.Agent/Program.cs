using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using GrayMint.Common.AspNetCore;
using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using GrayMint.Common.AspNetCore.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Persistence;
using VpnHood.AccessServer.Agent.Services;
using NLog.Web;
using NLog;

namespace VpnHood.AccessServer.Agent;

public class Program
{
    public static async Task Main(string[] args)
    {
        // nLog
        LogManager.Setup();

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("App"));
        builder.AddGrayMintCommonServices(builder.Configuration.GetSection("App"), new RegisterServicesOptions { AddSwaggerVersioning = false });
        builder.Services
             .AddAuthentication()
             .AddBotAuthentication(builder.Configuration.GetSection("Auth"), builder.Environment.IsProduction());

        builder.Services
            .AddDbContextPool<VhContext>(options =>
        {
            options.ConfigureWarnings(x => x.Ignore(RelationalEventId.MultipleCollectionIncludeWarning));
            options.UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase"));
        }, 100);

        builder.Services.AddScoped<SessionService>();
        builder.Services.AddScoped<CacheService>();
        builder.Services.AddScoped<AgentService>();
        builder.Services.AddScoped<IBotAuthenticationProvider, BotAuthenticationProvider>();
        builder.Services.AddHostedService<TimedHostedService>();

        // NLog: Setup NLog for Dependency injection
        builder.Logging.ClearProviders();
        builder.Host.UseNLog();

        //---------------------
        // Create App
        //---------------------
        var webApp = builder.Build();
        webApp.UseGrayMintCommonServices(new UseServicesOptions { UseAppExceptions = false });
        webApp.UseGrayMintExceptionHandler(new GrayMintExceptionHandlerOptions { RootNamespace = nameof(VpnHood) });
        await GrayMintApp.CheckDatabaseCommand<VhContext>(webApp, args);

        // Log Configs
        var logger = webApp.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("App: {Config}",
            JsonSerializer.Serialize(webApp.Services.GetRequiredService<IOptions<AgentOptions>>().Value, new JsonSerializerOptions { WriteIndented = true }));

        // init cache
        await using (var scope = webApp.Services.CreateAsyncScope())
        {
            var cacheService = scope.ServiceProvider.GetRequiredService<CacheService>();
            await cacheService.Init(false);
        }

        await GrayMintApp.RunAsync(webApp, args);
        LogManager.Shutdown();
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