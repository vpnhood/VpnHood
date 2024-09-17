using System.Net.Http.Headers;
using System.Security.Claims;
using GrayMint.Authorization.Authentications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using VpnHood.AccessServer.Agent;
using VpnHood.AccessServer.Agent.Services;
using VpnHood.AccessServer.Test.Helper;
using VpnHood.Common.IpLocations;

namespace VpnHood.AccessServer.Test;

public class AgentTestApp : IDisposable
{
    public WebApplicationFactory<Agent.Program> AgentApp { get; }
    public IServiceScope AgentScope { get; }
    public CacheService CacheService => AgentScope.ServiceProvider.GetRequiredService<CacheService>();
    public AgentOptions AgentOptions => AgentApp.Services.GetRequiredService<IOptions<AgentOptions>>().Value;
    public HttpClient HttpClient { get; }

    public AgentTestApp(Dictionary<string, string?> appSettings, string environment)
    {
        AgentApp = new WebApplicationFactory<Agent.Program>()
            .WithWebHostBuilder(builder => {
                foreach (var appSetting in appSettings)
                    builder.UseSetting(appSetting.Key, appSetting.Value);
                builder.UseSetting(nameof(AgentOptions.AllowRedirect), "false");

                builder.UseEnvironment(environment);
                builder.ConfigureServices(services => {
                    services.AddKeyedSingleton<IIpLocationProvider,
                        TestIpLocationProvider>(Agent.Program.LocationProviderServer,
                        (_, _) => new TestIpLocationProvider());

                    services.AddKeyedSingleton<IIpLocationProvider,
                        TestIpLocationProvider>(Agent.Program.LocationProviderDevice,
                        (_, _) => new TestIpLocationProvider());
                });
            });

        AgentScope = AgentApp.Services.CreateScope();
        AgentOptions.AllowRedirect = false;

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

    public void Dispose()
    {
        AgentApp.Dispose();
    }
}