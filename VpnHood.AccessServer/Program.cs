using System.Net.Http.Headers;
using System.Text.Json;
using GrayMint.Common.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Services;
using GrayMint.Common.Utils;
using VpnHood.AccessServer.Report;
using VpnHood.AccessServer.Security;
using NLog;
using NLog.Web;
using VpnHood.AccessServer.Report.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using GrayMint.Common.Swagger;
using GrayMint.Authorization;
using GrayMint.Authorization.RoleManagement.RoleProviders.Dtos;

namespace VpnHood.AccessServer;

public class Program
{
    public static async Task Main(string[] args)
    {
        // nLog
        LogManager.Setup();
        var builder = WebApplication.CreateBuilder(args);

        // app options
        var appOptions = builder.Configuration.GetSection("App").Get<AppOptions>() ?? throw new Exception("Could not load AppOptions.");
        builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));

        // Graymint
        builder.Services
            .AddGrayMintCommonServices(new RegisterServicesOptions())
            .AddGrayMintSwagger("VpnHood Access Manager", true);

        // add authentication
        builder.AddGrayMintCommonAuthorizationForApp(
            GmRole.GetAll(typeof(Roles)),
            options => options.UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase")));

        // Database
        builder.Services.AddDbContextPool<VhContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase")), 50);

        // HttpClient
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
        builder.Services.AddScoped<CertificateService>();
        builder.Services.AddScoped<UsageCycleService>();
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
        webApp.UseGrayMintSwagger(true);
        await webApp.Services.UseGrayMintDatabaseCommand<VhContext>(args);
        await webApp.UseGrayMinCommonAuthorizationForApp();
        await webApp.Services.UseVhReportServices(args);


        // Log Configs
        var logger = webApp.Services.GetRequiredService<ILogger<Program>>();
        var configJson = JsonSerializer.Serialize(webApp.Services.GetRequiredService<IOptions<AppOptions>>().Value, new JsonSerializerOptions { WriteIndented = true });
        logger.LogInformation("App: {Config}", GmUtil.RedactJsonValue(configJson, new[] { nameof(AppOptions.AgentSystemAuthorization) }));

        await GrayMintApp.RunAsync(webApp, args);
        LogManager.Shutdown();
    }
}