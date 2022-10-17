using Microsoft.EntityFrameworkCore;
using GrayMint.Common.AspNetCore;
using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using VpnHood.AccessServer.Agent.Persistence;
using VpnHood.AccessServer.Agent.Repos;

namespace VpnHood.AccessServer.Agent;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.RegisterAppCommonServices(builder.Configuration.GetSection("Agent"), new RegisterServicesOptions());

        builder.Services.AddAuthentication()
            .AddBotAuthentication(builder.Configuration.GetSection("Auth"), builder.Environment.IsProduction());

        builder.Services.AddDbContextPool<VhContext>(options =>
        {
            options.UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase"));
            //options.EnableSensitiveDataLogging();
        });

        builder.Services.AddScoped<SessionRepo>();
        builder.Services.AddScoped<CacheRepo>();
        builder.Services.AddScoped<IBotAuthenticationProvider, BotAuthenticationProvider>();

        //---------------------
        // Create App
        //---------------------
        var webApp = builder.Build();
        webApp.UseAppCommonServices(new UseServicesOptions());
        await AppCommon.CheckDatabaseCommand<VhContext>(webApp, args);

        await webApp.RunAsync();
    }
}