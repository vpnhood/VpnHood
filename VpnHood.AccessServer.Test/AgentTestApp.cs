﻿using GrayMint.Authorization.Authentications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using VpnHood.AccessServer.Agent;
using VpnHood.AccessServer.Agent.Services;
using System.Net.Http.Headers;

namespace VpnHood.AccessServer.Test;

public class AgentTestApp
{
    public WebApplicationFactory<Agent.Program> AgentApp { get; }
    public IServiceScope AgentScope { get; }
    public CacheService CacheService => AgentScope.ServiceProvider.GetRequiredService<CacheService>();
    public AgentOptions AgentOptions => AgentApp.Services.GetRequiredService<IOptions<AgentOptions>>().Value;
    public HttpClient HttpClient { get; }

    public AgentTestApp(Dictionary<string, string?> appSettings, string environment)
    {
        AgentApp = new WebApplicationFactory<Agent.Program>()
            .WithWebHostBuilder(builder =>
            {
                foreach (var appSetting in appSettings)
                    builder.UseSetting(appSetting.Key, appSetting.Value);

                builder.UseEnvironment(environment);
                builder.ConfigureServices(services =>
                {
                    _ = services;
                });
            });
        
        AgentScope = AgentApp.Services.CreateScope();
        AgentOptions.AllowRedirect = false; //to move to settings
        
        HttpClient = AgentApp.CreateClient();
        HttpClient.DefaultRequestHeaders.Authorization = GetAuthenticationHeaderValue(AgentApp.Services);
    }

    private static AuthenticationHeaderValue GetAuthenticationHeaderValue(IServiceProvider services)
    {
        var claimIdentity = new ClaimsIdentity();
        claimIdentity.AddClaim(new Claim("usage_type", "system"));
        claimIdentity.AddClaim(new Claim(JwtRegisteredClaimNames.Sub, "system"));
        claimIdentity.AddClaim(new Claim(ClaimTypes.Role, "System"));
        var scope = services.CreateScope();
        var grayMintAuthentication = scope.ServiceProvider.GetRequiredService<GrayMintAuthentication>();
        var authorization = grayMintAuthentication.CreateAuthenticationHeader(claimIdentity).Result;
        return authorization;
    }
}