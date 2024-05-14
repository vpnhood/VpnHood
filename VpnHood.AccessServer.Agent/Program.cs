using System.Text.Json;
using GrayMint.Authorization.Abstractions;
using GrayMint.Authorization.Authentications;
using GrayMint.Common.AspNetCore;
using GrayMint.Common.AspNetCore.ApplicationLifetime;
using GrayMint.Common.AspNetCore.Jobs;
using GrayMint.Common.Swagger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Agent.Services;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer.Agent;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var agentOptions = builder.Configuration.GetSection("App").Get<AgentOptions>() ?? throw new Exception("Could not read AgentOptions.");
        builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("App"));
        builder.Services.AddGrayMintCommonServices(new RegisterServicesOptions());
        builder.Services.AddGrayMintSwagger("VpnHood Agent Server", false);
        builder.Services.AddGrayMintJob<CacheService>(new GrayMintJobOptions
        {
            DueTime = agentOptions.SaveCacheInterval,
            Interval = agentOptions.SaveCacheInterval
        });

        // logger
        builder.Logging.AddSimpleConsole(c =>
        {
            c.TimestampFormat = "[HH:mm:ss] ";
        });

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

        // DbContext
        builder.Services
            .AddDbContextPool<VhContext>(options =>
            {
                options.UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase"));
            }, 100);


        builder.Services
            .AddGrayMintApplicationLifetime<CacheService>();

        builder.Services.AddSingleton<CacheRepo>();
        builder.Services.AddScoped<VhRepo>();
        builder.Services.AddScoped<VhAgentRepo>();
        builder.Services.AddScoped<SessionService>();
        builder.Services.AddScoped<CacheService>();
        builder.Services.AddScoped<AgentService>();
        builder.Services.AddScoped<LoadBalancerService>();
        builder.Services.AddScoped<IAuthorizationProvider, AgentAuthorizationProvider>();

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
    }

}