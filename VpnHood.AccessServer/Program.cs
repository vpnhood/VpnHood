using System.Net.Http.Headers;
using System.Text.Json;
using GrayMint.Authorization;
using GrayMint.Authorization.RoleManagement.RoleProviders.Dtos;
using GrayMint.Common.AspNetCore;
using GrayMint.Common.AspNetCore.Jobs;
using GrayMint.Common.Swagger;
using GrayMint.Common.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NLog.Web;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.Options;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Report;
using VpnHood.AccessServer.Report.Services;
using VpnHood.AccessServer.Repos;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;
using VpnHood.AccessServer.Services.Acme;

namespace VpnHood.AccessServer;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        try {
            // logger
            builder.Logging.AddSimpleConsole(c => { c.TimestampFormat = "[HH:mm:ss] "; });

            // NLog: Setup NLog for Dependency injection
            builder.Logging.ClearProviders();
            builder.Host.UseNLog();

            // app options
            var appOptions = builder.Configuration.GetSection("App").Get<AppOptions>() ??
                             throw new Exception("Could not load AppOptions.");
            var certificateValidatorOptions =
                builder.Configuration.GetSection("CertificateValidator").Get<CertificateValidatorOptions>() ??
                new CertificateValidatorOptions();
            builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
            builder.Services.Configure<CertificateValidatorOptions>(
                builder.Configuration.GetSection("CertificateValidator"));

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
            builder.Services.AddHttpClient(AppOptions.AgentHttpClientName, httpClient => {
                if (string.IsNullOrEmpty(appOptions.AgentSystemAuthorization))
                    GrayMintApp.ThrowOptionsValidationException(nameof(AppOptions.AgentSystemAuthorization),
                        typeof(string));

                httpClient.BaseAddress = appOptions.AgentUrlPrivate ?? appOptions.AgentUrl;
                httpClient.DefaultRequestHeaders.Authorization =
                    AuthenticationHeaderValue.Parse(appOptions.AgentSystemAuthorization);
            });

            // Update Cycle every 1 hour
            builder.Services
                .AddGrayMintJob<CertificateValidatorService>(
                    new GrayMintJobOptions // validate certificate every 24 hours
                    {
                        DueTime = certificateValidatorOptions.Due,
                        Interval = certificateValidatorOptions.Interval
                    })
                .AddGrayMintJob<UsageCycleService>(
                    new GrayMintJobOptions {
                        DueTime = appOptions.AutoMaintenanceInterval,
                        Interval = appOptions.AutoMaintenanceInterval
                    })
                .AddGrayMintJob<SyncService>(
                    new GrayMintJobOptions {
                        DueTime = appOptions.AutoMaintenanceInterval,
                        Interval = appOptions.AutoMaintenanceInterval
                    });

            builder.Services
                .AddHttpClient()
                .AddScoped<VhRepo>()
                .AddScoped<SyncService>()
                .AddScoped<ProjectService>()
                .AddScoped<ServerFarmService>()
                .AddScoped<ServerProfileService>()
                .AddScoped<ServerService>()
                .AddScoped<ServerConfigureService>()
                .AddScoped<SubscriptionService>()
                .AddScoped<CertificateService>()
                .AddScoped<CertificateValidatorService>()
                .AddScoped<UsageCycleService>()
                .AddScoped<AgentCacheClient>()
                .AddScoped<AgentSystemClient>()
                .AddScoped<AccessTokensService>()
                .AddScoped<ReportService>()
                .AddSingleton<IAcmeOrderFactory, AcmeOrderFactory>();

            // Report Service
            builder.Services.AddVhReportServices(new ReportServiceOptions {
                ConnectionString = builder.Configuration.GetConnectionString("VhReportDatabase") ??
                                   throw new Exception("Could not find VhReportDatabase."),
                ServerUpdateStatusInterval = appOptions.ServerUpdateStatusInterval
            });

            //---------------------
            // Create App
            //---------------------
            var webApp = builder.Build();

            webApp.UseGrayMintCommonServices(new UseServicesOptions { UseAppExceptions = false });
            webApp.UseGrayMintExceptionHandler(new GrayMintExceptionHandlerOptions { RootNamespace = nameof(VpnHood) });
            webApp.UseGrayMintSwagger(true);
            await webApp.Services.UseGrayMintDatabaseCommand<VhContext>(args);
            await webApp.UseGrayMinCommonAuthorizationForApp();
            await webApp.Services.UseVhReportServices(args);

            // Log Configs
            var logger = webApp.Services.GetRequiredService<ILogger<Program>>();
            var configJson = JsonSerializer.Serialize(webApp.Services.GetRequiredService<IOptions<AppOptions>>().Value,
                new JsonSerializerOptions { WriteIndented = true });
            logger.LogInformation("App: {Config}",
                GmUtil.RedactJsonValue(configJson, [nameof(AppOptions.AgentSystemAuthorization)]));

            await GrayMintApp.RunAsync(webApp, args);
        }
        finally {
            NLog.LogManager.Shutdown();
        }
    }

    //private async Task Migrate(ILogger logger, WebApplication webApp)
    //{
    //    logger.LogInformation("Upgrading..");
    //    var scope = webApp.Services.CreateAsyncScope();
    //    await using var context = scope.ServiceProvider.GetRequiredService<VhContext>();
    //    context.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
    //    var projects = await context.Projects.Where(x => string.IsNullOrEmpty(x.AdRewardSecret)).ToArrayAsync();
    //    logger.LogInformation($"ProjectCount: {projects.Length}..");
    //    foreach (var project in projects)
    //        project.AdRewardSecret = Convert.ToBase64String(GmUtil.GenerateKey())
    //            .Replace("/", "")
    //            .Replace("+", "")
    //            .Replace("=", "");
    //    await context.SaveChangesAsync();
    //    logger.LogInformation($"Finish migrate: {projects.Length}..");
    //}
}