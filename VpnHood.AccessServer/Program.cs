using System.Linq;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using GrayMint.Common.AspNetCore;
using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Web;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.MultiLevelAuthorization;
using VpnHood.AccessServer.MultiLevelAuthorization.Persistence;
using VpnHood.AccessServer.MultiLevelAuthorization.Repos;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var appOptions = builder.Configuration.GetSection("App").Get<AppOptions>();
        builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));

        builder.AddGrayMintCommonServices(builder.Configuration.GetSection("App"), new RegisterServicesOptions() { AddSwaggerVersioning = false });

        builder.Services.AddAuthentication("AzureB2C")
            .AddBotAuthentication(builder.Configuration.GetSection("Auth"), builder.Environment.IsProduction())
            .AddMicrosoftIdentityWebApi(jwtBearerOptions =>
            {
                jwtBearerOptions.Events = new JwtBearerEvents()
                {
                    OnTokenValidated = context =>
                    {
                        var claimsIdentity = new ClaimsIdentity();
                        var email = context.Principal?.Claims.FirstOrDefault(claim => claim.Type == "emails")?.Value;
                        if (email != null)
                        {
                            claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, email));
                            context.Principal!.AddIdentity(claimsIdentity);
                        }
                        return Task.CompletedTask;
                    }
                };
            },
                microsoftIdentityOptions =>
            {
                builder.Configuration.GetSection("AzureB2C").Bind(microsoftIdentityOptions);
                //microsoftIdentityOptions.ClientId = "3aae4934-4c35-428b-92df-a5ed9e3f8787";
                //microsoftIdentityOptions.Instance = "https://vpnhoodcom.b2clogin.com";
                //microsoftIdentityOptions.Domain = "vpnhoodcom.onmicrosoft.com";
                //microsoftIdentityOptions.SignUpSignInPolicyId = "B2C_1_signup_signin";
            }, "AzureB2C");

        builder.Services.AddMultilevelAuthorization();

        builder.Services.AddDbContextPool<VhContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase")));

        builder.Services.AddDbContext<VhReportContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("VhReportDatabase")));

        builder.Services.AddDbContext<MultilevelAuthContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase")));

        builder.Services.AddHostedService<TimedHostedService>();
        builder.Services.AddSingleton<UsageCycleManager>();
        builder.Services.AddSingleton<SyncManager>();
        builder.Services.AddHttpClient(AppOptions.AgentHttpClientName, httpClient =>
        {
            httpClient.BaseAddress = appOptions.AgentUri;
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(JwtBearerDefaults.AuthenticationScheme, appOptions.AgentAuthorization);
        });
        builder.Services.AddScoped<AgentCacheClient>();
        builder.Services.AddScoped<AgentSystemClient>();
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
            var authRepo = scope.ServiceProvider.GetRequiredService<MultilevelAuthRepo>();
            await authRepo.Init(SecureObjectTypes.All, Permissions.All, PermissionGroups.All);
        }

        await AppCommon.RunAsync(webApp, args);
    }
}