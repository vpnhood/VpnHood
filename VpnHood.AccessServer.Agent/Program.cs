using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using GrayMint.Common.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Agent.Services;
using NLog.Web;
using NLog;
using Microsoft.AspNetCore.Authorization;
using GrayMint.Authorization.Abstractions;
using GrayMint.Authorization.Authentications;
using GrayMint.Authorization.Authentications.Utils;
using GrayMint.Common.Swagger;

namespace VpnHood.AccessServer.Agent;

public class Program
{
    public static async Task Main(string[] args)
    {
        // nLog
        LogManager.Setup();

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("App"));
        builder.Services.AddGrayMintCommonServices(new RegisterServicesOptions());
        builder.Services.AddGrayMintSwagger("VpnHood Agent Server", false);

        //Authentication
        builder.Services
             .AddAuthentication()
             .AddGrayMintAuthentication(builder.Configuration.GetSection("Auth").Get<GrayMintAuthenticationOptions>()!,
                 builder.Environment.IsProduction());

        // Authorization Policies
        builder.Services
            .AddAuthorization(options =>
            {
                var policy = new AuthorizationPolicyBuilder()
                    .AddAuthenticationSchemes(GrayMintAuthenticationDefaults.AuthenticationScheme)
                    .RequireRole("System")
                    .RequireAuthenticatedUser()
                    .Build();
                options.AddPolicy(AgentPolicy.SystemPolicy, policy);
                options.DefaultPolicy = policy;

                policy = new AuthorizationPolicyBuilder()
                    .AddAuthenticationSchemes(GrayMintAuthenticationDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    .Build();
                options.AddPolicy(AgentPolicy.VpnServerPolicy, policy);
            });

        builder.Services
            .AddDbContextPool<VhContext>(options =>
            {
                options.ConfigureWarnings(x => x.Ignore(RelationalEventId.MultipleCollectionIncludeWarning));
                options.UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase"));
            }, 100);

        builder.Services.AddScoped<SessionService>();
        builder.Services.AddScoped<CacheService>();
        builder.Services.AddScoped<AgentService>();
        builder.Services.AddScoped<IAuthorizationProvider, AgentAuthorizationProvider>();
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
        webApp.UseGrayMintSwagger(true);
        await webApp.Services.UseGrayMintDatabaseCommand<VhContext>(args);

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