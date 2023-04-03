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
using GrayMint.Common.Utils;
using GrayMint.Common.AspNetCore.Auth.CognitoAuthentication;
using GrayMint.Common.AspNetCore.SimpleRoleAuthorization;
using GrayMint.Common.AspNetCore.SimpleUserManagement;
using VpnHood.AccessServer.Report;
using VpnHood.AccessServer.Security;
using NLog;
using NLog.Web;
using VpnHood.AccessServer.Report.Services;

namespace VpnHood.AccessServer;

public class Program
{
    public static async Task Main(string[] args)
    {
        // nLog
        LogManager.Setup();
        var builder = WebApplication.CreateBuilder(args);
        var appOptions = builder.Configuration.GetSection("App").Get<AppOptions>() ?? throw new Exception("Could not load AppOptions.");
        var authConfiguration = builder.Configuration.GetSection("Auth");
        var isTest = Environment.GetEnvironmentVariable("IsTest") == true.ToString();

        builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
        builder.AddGrayMintCommonServices(
            new GrayMintCommonOptions { AppName = "VpnHood Access Server" }, 
            new RegisterServicesOptions());

        // add authentication
        var authenticationBuilder = builder.Services
            .AddAuthentication()
            .AddBotAuthentication(authConfiguration.Get<BotAuthenticationOptions>(), builder.Environment.IsProduction());
        
        if (!isTest)
            authenticationBuilder.AddCognitoAuthentication(authConfiguration.Get<CognitoAuthenticationOptions>());

        // Add authentications
        builder.Services.AddGrayMintSimpleRoleAuthorization(new SimpleRoleAuthOptions{ResourceParamName="projectId", Roles = Roles.All});
        builder.Services.AddGrayMintSimpleUserProvider(authConfiguration.Get<SimpleUserOptions>(), options => options.UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase")));

        builder.Services.AddDbContextPool<VhContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase")), 50);

        builder.Services.AddHttpClient(AppOptions.AgentHttpClientName, httpClient =>
        {
            if (string.IsNullOrEmpty(appOptions.AgentSystemAuthorization))
                GrayMintApp.ThrowOptionsValidationException(nameof(AppOptions.AgentSystemAuthorization), typeof(string));

            httpClient.BaseAddress = appOptions.AgentUrlPrivate ?? appOptions.AgentUrl;
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(JwtBearerDefaults.AuthenticationScheme, appOptions.AgentSystemAuthorization);
        });

        builder.Services.AddHostedService<TimedHostedService>();
        builder.Services.AddScoped<SyncService>();
        builder.Services.AddScoped<ProjectService>();
        builder.Services.AddScoped<ServerFarmService>();
        builder.Services.AddScoped<ServerProfileService>();
        builder.Services.AddScoped<ServerService>();
        builder.Services.AddScoped<SubscriptionService>();
        builder.Services.AddScoped<UsageCycleService>();
        builder.Services.AddScoped<UserService>();
        builder.Services.AddScoped<AgentCacheClient>();
        builder.Services.AddScoped<AgentSystemClient>();

        // Report Service
        builder.Services.AddVhReportServices(new ReportServiceOptions
        {
            ConnectionString = builder.Configuration.GetConnectionString("VhReportDatabase") ?? throw new Exception("Could not find VhReportDatabase."),
            ServerUpdateStatusInterval = appOptions.ServerUpdateStatusInterval
        });

        // NLog: Setup NLog for Dependency injection
        builder.Logging.ClearProviders();
        builder.Host.UseNLog();

        //---------------------
        // Create App
        //---------------------
        var webApp = builder.Build();
        webApp.UseGrayMintCommonServices(new UseServicesOptions() { UseAppExceptions = false });
        webApp.UseGrayMintExceptionHandler(new GrayMintExceptionHandlerOptions { RootNamespace = nameof(VpnHood) });
        await GrayMintApp.CheckDatabaseCommand<VhContext>(webApp.Services, args);
        webApp.ScheduleGrayMintSqlMaintenance<VhContext>(TimeSpan.FromDays(2));
        await webApp.UseGrayMintSimpleUserProvider();
        await webApp.Services.UseVhReportServices(args);

        // Log Configs
        var logger = webApp.Services.GetRequiredService<ILogger<Program>>();
        var configJson = JsonSerializer.Serialize(webApp.Services.GetRequiredService<IOptions<AppOptions>>().Value, new JsonSerializerOptions { WriteIndented = true });
        logger.LogInformation("App: {Config}", GmUtil.RedactJsonValue(configJson, new[] { nameof(AppOptions.AgentSystemAuthorization) }));

        await GrayMintApp.RunAsync(webApp, args);
        LogManager.Shutdown();
    }
}