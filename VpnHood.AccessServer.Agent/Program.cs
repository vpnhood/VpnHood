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
using NLog.Web;
using VpnHood.AccessServer.Agent.Repos;
using VpnHood.AccessServer.Agent.Services;
using VpnHood.AccessServer.Agent.Services.IpLocationServices;
using VpnHood.AccessServer.Persistence;
using VpnHood.Common.IpLocations;

namespace VpnHood.AccessServer.Agent;

public class Program
{
    public const string LocationProviderServer = "ServerLocationProvider";
    public const string LocationProviderDevice = "ServerLocationDevice";

    public static async Task Main(string[] args)
    {
        try {
            var builder = WebApplication.CreateBuilder(args);

            // logger (Microsoft)
            builder.Logging.AddSimpleConsole(c => { c.TimestampFormat = "[HH:mm:ss] "; });

            // NLog: Setup NLog for Dependency injection
            if (Environment.OSVersion.Platform == PlatformID.Unix) {
                builder.Logging.ClearProviders();
                builder.Host.UseNLog();
            }

            var agentOptions = builder.Configuration.GetSection("App").Get<AgentOptions>() ??
                               throw new Exception("Could not read AgentOptions.");
            builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("App"));
            builder.Services.AddGrayMintCommonServices(new RegisterServicesOptions());
            builder.Services.AddGrayMintSwagger("VpnHood Agent Server", false);

            builder.Services
                .AddGrayMintJob<CacheService>(new GrayMintJobOptions {
                    DueTime = agentOptions.SaveCacheInterval,
                    Interval = agentOptions.SaveCacheInterval
                })
                .AddGrayMintJob<FarmTokenRepoUploader>(new GrayMintJobOptions {
                    DueTime = agentOptions.FarmTokenRepoUpdaterInterval,
                    Interval = agentOptions.FarmTokenRepoUpdaterInterval
                });


            //Authentication
            builder.Services
                .AddAuthentication()
                .AddGrayMintAuthentication(
                    builder.Configuration.GetSection("Auth").Get<GrayMintAuthenticationOptions>()!,
                    builder.Environment.IsProduction());

            // Authorization Policies
            builder.Services
                .AddAuthorization(options => {
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
                .AddDbContextPool<VhContext>(
                    options => { options.UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase")); }, 
                    poolSize: 40); //max pool size for sql server is 60


            builder.Services
                .AddHttpClient()
                .AddGrayMintApplicationLifetime<CacheService>()
                .AddSingleton<CacheRepo>()
                .AddScoped<VhAgentRepo>()
                .AddScoped<SessionService>()
                .AddScoped<CacheService>()
                .AddScoped<AgentService>()
                .AddScoped<ServerSelectorService>()
                .AddScoped<FarmTokenUpdater>()
                .AddScoped<FarmTokenRepoUploader>()
                .AddKeyedSingleton<IIpLocationProvider, DeviceIpLocationProvider>(LocationProviderDevice)
                .AddKeyedSingleton<IIpLocationProvider, ServerIpLocationProvider>(LocationProviderServer)
                .AddScoped<IAuthorizationProvider, AgentAuthorizationProvider>();

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
                JsonSerializer.Serialize(webApp.Services.GetRequiredService<IOptions<AgentOptions>>().Value,
                    new JsonSerializerOptions { WriteIndented = true }));

            // init cache
            await using (var scope = webApp.Services.CreateAsyncScope()) {
                var cacheService = scope.ServiceProvider.GetRequiredService<CacheService>();
                await cacheService.Init(false);
            }


            await GrayMintApp.RunAsync(webApp, args);
        }
        finally {
            NLog.LogManager.Shutdown();
        }
    }

    //private static async Task Migrate(ILogger logger, WebApplication webApp)
    //{
    //    logger.LogInformation("Upgrading..");
    //    var scope = webApp.Services.CreateAsyncScope();
    //    await using var context = scope.ServiceProvider.GetRequiredService<VhContext>();
    //    var vhAgentRepo = scope.ServiceProvider.GetRequiredService<VhAgentRepo>();
    //    var locationService = scope.ServiceProvider.GetRequiredKeyedService<IIpLocationProvider>(LocationProviderServer);

    //    var servers = await context.Servers.Where(x => x.LocationId != null && x.LocationId < 300).ToArrayAsync();
    //    foreach (var server in servers) {

    //        try {
    //            var ip = server.PublicIpV4 ?? server.PublicIpV6 ?? throw new Exception("no ip");
    //            var location = await locationService.GetLocation(IPAddress.Parse(ip), CancellationToken.None);
    //            var locationModel = await vhAgentRepo.LocationFind(countryCode: location.CountryCode, regionName: location.RegionName,
    //                cityName: location.CityName);
    //            locationModel ??= await vhAgentRepo.LocationAdd(new LocationModel {
    //                LocationId = 0,
    //                CityName = location.CityName,
    //                CountryCode = location.CountryCode,
    //                RegionName = location.RegionName,
    //                ContinentCode = location.CountryCode,
    //                CountryName = location.CountryName,
    //            });
    //            server.Location = locationModel;
    //        }
    //        catch (Exception e) {
    //            server.LocationId = null;
    //            Console.WriteLine(e);
    //        }

    //        await context.SaveChangesAsync();
    //    }

    //}
}