using Microsoft.EntityFrameworkCore;
using GrayMint.Common.AspNetCore;
using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using VpnHood.AccessServer.Agent.Caching;
using VpnHood.AccessServer.Agent.Persistence;
using VpnHood.AccessServer.Agent.Repos;

namespace VpnHood.AccessServer.Agent;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.RegisterAppCommonServices(builder.Configuration.GetSection("Agent"),
            builder.Configuration.GetSection("Auth"),
            new RegisterServicesOptions( ){AddBotAuthentication = false});

        builder.Services.AddAuthentication()
            .AddBotAuthentication(builder.Configuration.GetSection("Auth"), builder.Environment.IsProduction());

        builder.Services.AddDbContextPool<VhContext>(options =>
        {
            options.UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase"));
            //options.EnableSensitiveDataLogging();
        });

        builder.Services.AddSingleton<SessionRepo>();
        builder.Services.AddSingleton<SystemCache>();
        builder.Services.AddScoped<IBotAuthenticationProvider, BothAuthenticationProvider>();

        //---------------------
        // Create App
        //---------------------
        var webApp = builder.Build();
        webApp.UseAppCommonServices(new UseServicesOptions());
        await AppCommon.CheckDatabaseCommand<VhContext>(webApp, args);

        await webApp.RunAsync();
    }
}