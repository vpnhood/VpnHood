using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        using var accessServerApp = AccessServerApp.IsInit ? AccessServerApp.Instance : new AccessServerApp(); //todo

        //enable cross-origin; MUST before anything
        builder.Services.AddCors(o => o.AddPolicy("CorsPolicy", corsPolicyBuilder =>
        {
            corsPolicyBuilder
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader()
                .SetPreflightMaxAge(TimeSpan.FromHours(24 * 30));
        }));

        // Add authentications
        var securityKey = new SymmetricSecurityKey(Application.GetAuthenticationKey(builder.Configuration));
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(Application.AuthRobotScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "name",
                    RequireSignedTokens = true,
                    IssuerSigningKey = securityKey,
                    ValidIssuer = Application.AuthIssuer,
                    ValidAudience = Application.AuthAudience,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateIssuer = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(TokenValidationParameters.DefaultClockSkew.TotalSeconds),
                };
            })
            .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureB2C"));

        builder.Services.AddControllers(options =>
        {
            options.ModelMetadataDetailsProviders.Add(
                new SuppressChildValidationMetadataProvider(typeof(IPAddress)));
        });

        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddAppSwaggerGen();
        builder.Services.AddMemoryCache();
        builder.Services.AddDbContext<VhContext>(options => options .UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase")));
        builder.Services.AddDbContext<VhReportContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("VhReportDatabase")));
        builder.Services.AddHostedService<TimedHostedService>();

        builder.Services.AddSingleton<ServerManager>();
        builder.Services.AddSingleton<UsageCycleManager>();
        builder.Services.AddSingleton<SyncManager>();
        builder.Services.AddSingleton<Application>();

        //---------------------
        // Create App
        //---------------------
        var webApp = builder.Build();
        var logger = webApp.Services.GetRequiredService<ILogger<Program>>();

        // Cors must configure before any Authorization to allow token request
        webApp.UseCors("CorsPolicy");

        // Configure the HTTP request pipeline.
        webApp.UseSwagger();
        webApp.UseSwaggerUI();

        webApp.UseHttpsRedirection();
        webApp.UseAuthorization();
        webApp.MapControllers();
        webApp.UseAppExceptionHandler();

        //---------------------
        // Initializing App
        //---------------------
        using var scope = webApp.Services.CreateScope();
        var vhContext = scope.ServiceProvider.GetRequiredService<VhContext>();
        var vhReportContext = scope.ServiceProvider.GetRequiredService<VhReportContext>();
        if (args.Contains("/recreatedb"))
        {
            logger.LogInformation($"Recreating the {nameof(VhContext)} database...");
            await vhContext.Database.EnsureDeletedAsync();
            await vhContext.Database.EnsureCreatedAsync();

            logger.LogInformation($"Recreating the {nameof(VhReportContext)} database...");
            await vhReportContext.Database.EnsureDeletedAsync();
            await vhReportContext.Database.EnsureCreatedAsync();
            return;
        }

        // initializing database
        logger.LogInformation($"Initializing databases...");
        await vhContext.Database.EnsureCreatedAsync();
        await vhReportContext.Database.EnsureCreatedAsync();
        await vhContext.Init(SecureObjectTypes.All, Permissions.All, PermissionGroups.All);

        await webApp.RunAsync();
    }

    // ReSharper disable once UnusedMember.Global
    // https://docs.microsoft.com/en-us/ef/core/cli/dbcontext-creation?tabs=dotnet-core-cli
    // for design time support
    //public static IHostBuilder CreateHostBuilder(string[] args)
    //{
    //    return Host.CreateDefaultBuilder(args);
    //}
}