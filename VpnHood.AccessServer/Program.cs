using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using GrayMint.Common.AspNetCore;
using GrayMint.Common.Swagger;
using GrayMint.Authorization;
using GrayMint.Authorization.RoleManagement.RoleProviders.Dtos;
using GrayMint.Common.Utils;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Services;
using VpnHood.AccessServer.Report;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Report.Services;

namespace VpnHood.AccessServer;

public class Program
{
    public static async Task Main(string[] args)
    {
        // nLog
        //LogManager.Setup();
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
            httpClient.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(appOptions.AgentSystemAuthorization);
        });

        builder.Services.AddHttpClient();
        builder.Services.AddHostedService<TimedHostedService>();
        builder.Services.AddScoped<VhRepo>();
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
        builder.Services.AddScoped<AccessTokensService>();

        // Report Service
        builder.Services.AddVhReportServices(new ReportServiceOptions
        {
            ConnectionString = builder.Configuration.GetConnectionString("VhReportDatabase") ?? throw new Exception("Could not find VhReportDatabase."),
            ServerUpdateStatusInterval = appOptions.ServerUpdateStatusInterval
        });

        // NLog: Setup NLog for Dependency injection
        //builder.Logging.ClearProviders();
        //builder.Host.UseNLog();

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
        logger.LogInformation("App: {Config}", GmUtil.RedactJsonValue(configJson, [nameof(AppOptions.AgentSystemAuthorization)]));

        // upgrade
        if (builder.Configuration.GetValue<string>("IsTest") != "1")
        {
            await using var scope = webApp.Services.CreateAsyncScope();
            var farmService = scope.ServiceProvider.GetRequiredService<ServerFarmService>();
            await farmService.UpgradeAllFarmTokens();
        }

        await GrayMintApp.RunAsync(webApp, args);
    }
    //test
}