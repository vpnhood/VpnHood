﻿using System;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using GrayMint.Authorization.Authentications.BotAuthentication;
using GrayMint.Authorization.Authentications.CognitoAuthentication;
using GrayMint.Authorization.RoleManagement.RoleAuthorizations;
using GrayMint.Authorization.RoleManagement.SimpleRoleProviders;
using GrayMint.Authorization.UserManagement.SimpleUserProviders;
using GrayMint.Common.AspNetCore;
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
using VpnHood.AccessServer.Report;
using VpnHood.AccessServer.Security;
using NLog;
using NLog.Web;
using VpnHood.AccessServer.Report.Services;
using GrayMint.Authorization.RoleManagement.SimpleRoleProviders.Dtos;
using GrayMint.Authorization.RoleManagement.TeamControllers;

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
        builder.Services
            .AddAuthentication()
            .AddBotAuthentication(authConfiguration.Get<BotAuthenticationOptions>(), builder.Environment.IsProduction())
            .AddCognitoAuthentication(authConfiguration.Get<CognitoAuthenticationOptions>());

        // Add authentications
        var authenticationSchemes = isTest 
            ? new[] { BotAuthenticationDefaults.AuthenticationScheme }
            : new[] { BotAuthenticationDefaults.AuthenticationScheme, CognitoAuthenticationDefaults.AuthenticationScheme };
        builder.Services.AddGrayMintRoleAuthorization(new RoleAuthorizationOptions { ResourceParamName = "projectId", AuthenticationSchemes = authenticationSchemes });
        builder.Services.AddGrayMintSimpleRoleProvider(new SimpleRoleProviderOptions { Roles = SimpleRole.GetAll(typeof(Roles)) }, options => options.UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase")));
        builder.Services.AddGrayMintSimpleUserProvider(authConfiguration.Get<SimpleUserProviderOptions>(), options => options.UseSqlServer(builder.Configuration.GetConnectionString("VhDatabase")));
        builder.Services.AddGrayMintTeamController(builder.Configuration.GetSection("TeamController").Get<TeamControllerOptions>());

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
        await webApp.Services.UseGrayMintDatabaseCommand<VhContext>(args);
        await webApp.Services.UseGrayMintSimpleUserProvider();
        await webApp.Services.UseGrayMintSimpleRoleProvider();
        await webApp.Services.UseVhReportServices(args);

        // Log Configs
        var logger = webApp.Services.GetRequiredService<ILogger<Program>>();
        var configJson = JsonSerializer.Serialize(webApp.Services.GetRequiredService<IOptions<AppOptions>>().Value, new JsonSerializerOptions { WriteIndented = true });
        logger.LogInformation("App: {Config}", GmUtil.RedactJsonValue(configJson, new[] { nameof(AppOptions.AgentSystemAuthorization) }));

        await GrayMintApp.RunAsync(webApp, args);
        LogManager.Shutdown();
    }
}