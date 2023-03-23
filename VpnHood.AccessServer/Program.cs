using System;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using GrayMint.Common.AspNetCore;
using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Services;
using NLog;
using NLog.Web;
using GrayMint.Common.Utils;
using GrayMint.Common.AspNetCore.Auth.CognitoAuthentication;
using GrayMint.Common.AspNetCore.SimpleRoleAuthorization;
using GrayMint.Common.AspNetCore.SimpleUserManagement;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer;

public class Program
{
    public static async Task Main(string[] args)
    {
        // nLog
        LogManager.Setup();
        var builder = WebApplication.CreateBuilder(args);
        var appOptions = builder.Configuration.GetSection("App").Get<AppOptions>() ?? throw new Exception("Could not load AppOptions.");
        builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
        builder.AddGrayMintCommonServices(builder.Configuration.GetSection("App"), new RegisterServicesOptions());
        var useCognito = !builder.Environment.IsDevelopment();

        // add authentication
        var authenticationBuilder = builder.Services
            .AddAuthentication()
            .AddBotAuthentication(builder.Configuration.GetSection("Auth"), builder.Environment.IsProduction());

        if (useCognito)
            authenticationBuilder.AddCognitoAuthentication(builder.Configuration.GetSection("Auth"));

        // Add authentications
        builder.Services.AddGrayMintSimpleRoleAuthorization(new SimpleRoleAuthOptions{AppIdParamName="projectId", Roles = Roles.All});
        builder.Services.AddGrayMintSimpleUserProvider(options => options.UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase")));

        builder.Services.AddDbContextPool<VhContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase")), 50);

        builder.Services.AddDbContext<VhReportContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("VhReportDatabase")));

        builder.Services.AddHostedService<TimedHostedService>();
        builder.Services.AddSingleton<SyncService>();
        builder.Services.AddHttpClient(AppOptions.AgentHttpClientName, httpClient =>
        {
            if (string.IsNullOrEmpty(appOptions.AgentSystemAuthorization))
                GrayMintApp.ThrowOptionsValidationException(nameof(AppOptions.AgentSystemAuthorization), typeof(string));

            httpClient.BaseAddress = appOptions.AgentUrlPrivate ?? appOptions.AgentUrl;
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(JwtBearerDefaults.AuthenticationScheme, appOptions.AgentSystemAuthorization);
        });
        builder.Services.AddScoped<UsageCycleService>();
        builder.Services.AddScoped<AgentCacheClient>();
        builder.Services.AddScoped<AgentSystemClient>();
        builder.Services.AddScoped<UsageReportService>();
        builder.Services.AddScoped<ServerService>();
        builder.Services.AddScoped<ServerFarmService>();
        builder.Services.AddScoped<SubscriptionService>();
        builder.Services.AddScoped<ServerProfileService>();
        builder.Services.AddScoped<UserService>();

        // NLog: Setup NLog for Dependency injection
        builder.Logging.ClearProviders();
        builder.Host.UseNLog();

        //---------------------
        // Create App
        //---------------------
        var webApp = builder.Build();
        webApp.UseGrayMintCommonServices(new UseServicesOptions() { UseAppExceptions = false });
        webApp.UseGrayMintExceptionHandler(new GrayMintExceptionHandlerOptions { RootNamespace = nameof(VpnHood) });
        await GrayMintApp.CheckDatabaseCommand<VhContext>(webApp, args);
        await GrayMintApp.CheckDatabaseCommand<VhReportContext>(webApp, args);
        webApp.ScheduleGrayMintSqlMaintenance<VhContext>(TimeSpan.FromDays(2));
        webApp.ScheduleGrayMintSqlMaintenance<VhReportContext>(TimeSpan.FromDays(2));
        await webApp.UseGrayMintSimpleUserProvider();

        // Log Configs
        var logger = webApp.Services.GetRequiredService<ILogger<Program>>();
        var configJson = JsonSerializer.Serialize(webApp.Services.GetRequiredService<IOptions<AppOptions>>().Value, new JsonSerializerOptions { WriteIndented = true });
        logger.LogInformation("App: {Config}", GmUtil.RedactJsonValue(configJson, new[] { nameof(AppOptions.AgentSystemAuthorization) }));

        await GrayMintApp.RunAsync(webApp, args);
        LogManager.Shutdown();
    }
}