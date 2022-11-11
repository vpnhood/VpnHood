using System.Net.Http.Headers;
using System.Threading.Tasks;
using GrayMint.Common.AspNetCore;
using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.MultiLevelAuthorization;
using VpnHood.AccessServer.MultiLevelAuthorization.Persistence;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var appOptions = builder.Configuration.GetSection("App").Get<AppOptions>();
        builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));

        builder.AddGrayMintCommonServices(builder.Configuration.GetSection("App"), new RegisterServicesOptions() { AddSwaggerVersioning = false });

        builder.Services.AddAuthentication()
            .AddAzureB2CAuthentication(builder.Configuration.GetSection("AzureB2C"))
            .AddBotAuthentication(builder.Configuration.GetSection("Auth"), builder.Environment.IsProduction());

        builder.Services.AddMultilevelAuthorization();

        builder.Services.AddDbContextPool<VhContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase")), 50);

        builder.Services.AddDbContext<VhReportContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("VhReportDatabase")));

        builder.Services.AddDbContextPool<MultilevelAuthContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase")), 50);

        builder.Services.AddHostedService<TimedHostedService>();
        builder.Services.AddSingleton<UsageCycleService>();
        builder.Services.AddSingleton<SyncService>();
        builder.Services.AddHttpClient(AppOptions.AgentHttpClientName, httpClient =>
        {
            if (string.IsNullOrEmpty(appOptions.AgentSystemAuthorization))
                AppCommon.ThrowOptionsValidationException(nameof(AppOptions.AgentSystemAuthorization), typeof(string));
            httpClient.BaseAddress = appOptions.AgentUrlPrivate ?? appOptions.AgentUrl;
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(JwtBearerDefaults.AuthenticationScheme, appOptions.AgentSystemAuthorization);
        });
        builder.Services.AddScoped<AgentCacheClient>();
        builder.Services.AddScoped<AgentSystemClient>();
        builder.Services.AddScoped<UsageReportService>();
        builder.Services.AddScoped<IBotAuthenticationProvider, BotAuthenticationProvider>();

        //---------------------
        // Create App
        //---------------------
        var webApp = builder.Build();
        webApp.UseAppCommonServices(new UseServicesOptions());
        await AppCommon.CheckDatabaseCommand<VhContext>(webApp, args);
        await AppCommon.CheckDatabaseCommand<VhReportContext>(webApp, args);
        await webApp.UseMultilevelAuthorization();

        using (var scope = webApp.Services.CreateScope())
        {
            var authRepo = scope.ServiceProvider.GetRequiredService<MultilevelAuthService>();
            await authRepo.Init(SecureObjectTypes.All, Permissions.All, PermissionGroups.All);
        }

        await AppCommon.RunAsync(webApp, args);
    }
}