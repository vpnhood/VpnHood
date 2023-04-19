using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Agent;
using VpnHood.AccessServer.Agent.Services;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.Security;
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Server;
using VpnHood.Server.Messaging;
using VpnHood.AccessServer.Report.Persistence;
using System.Net.Http.Headers;
using GrayMint.Authorization.Abstractions;
using GrayMint.Authorization.Authentications.BotAuthentication;
using GrayMint.Authorization.RoleManagement.SimpleRoleProviders.Dtos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Test.Helper;

namespace VpnHood.AccessServer.Test;

[TestClass]
public class TestInit : IHttpClientFactory, IDisposable
{
    public WebApplicationFactory<Program> WebApp { get; }
    public WebApplicationFactory<Agent.Program> AgentApp { get; }

    public IServiceScope Scope { get; }
    public IServiceScope AgentScope { get; }
    public VhContext VhContext => Scope.ServiceProvider.GetRequiredService<VhContext>();
    public VhReportContext VhReportContext => Scope.ServiceProvider.GetRequiredService<VhReportContext>();
    public CacheService CacheService => AgentScope.ServiceProvider.GetRequiredService<CacheService>();
    public HttpClient HttpClient { get; }
    public AgentOptions AgentOptions => AgentApp.Services.GetRequiredService<IOptions<AgentOptions>>().Value;
    public AppOptions AppOptions => WebApp.Services.GetRequiredService<IOptions<AppOptions>>().Value;
    public AgentCacheClient AgentCacheClient => Scope.ServiceProvider.GetRequiredService<AgentCacheClient>();
    public AgentSystemClient AgentSystemClient => Scope.ServiceProvider.GetRequiredService<AgentSystemClient>();
    public ServerFarmsClient ServerFarmsClient => new(HttpClient);
    public ServersClient ServersClient => new(HttpClient);
    public CertificatesClient CertificatesClient => new(HttpClient);
    public AccessTokensClient AccessTokensClient => new(HttpClient);
    public ProjectsClient ProjectsClient => new(HttpClient);
    public IpLocksClient IpLocksClient => new(HttpClient);
    public AccessesClient AccessesClient => new(HttpClient);
    public DevicesClient DevicesClient => new(HttpClient);
    public SystemClient SystemClient => new(HttpClient);
    public ServerProfilesClient ServerProfilesClient => new(HttpClient);
    public TeamClient TeamClient => new(HttpClient);

    public UserApiKey SystemAdminApiKey { get; private set; } = default!;
    public UserApiKey ProjectOwnerApiKey { get; private set; } = default!;
    public Project Project { get; private set; } = default!;
    public Guid ProjectId => Project.ProjectId;
    public DateTime CreatedTime { get; } = DateTime.UtcNow;

    private static IPAddress _lastIp = IPAddress.Parse("1.0.0.0");

    private TestInit(Dictionary<string, string?> appSettings, string environment)
    {
        Environment.SetEnvironmentVariable("IsTest", true.ToString());
        WebApp = CreateWebApp<Program>(appSettings, environment);
        AgentApp = CreateWebApp<Agent.Program>(appSettings, environment);
        AgentOptions.AllowRedirect = false;
        Scope = WebApp.Services.CreateScope();
        AgentScope = AgentApp.Services.CreateScope();
        HttpClient = WebApp.CreateClient();
    }

    public async Task Init()
    {
        QuotaConstants.ProjectCount = 0xFFFFFF;
        QuotaConstants.ServerCount = 0xFFFFFF;
        QuotaConstants.CertificateCount = 0xFFFFFF;
        QuotaConstants.AccessTokenCount = 0xFFFFFF;
        QuotaConstants.AccessPointCount = 0xFFFFFF;
        QuotaConstants.ServerFarmCount = 0xFFFFFF;
        QuotaConstants.TeamUserCount = 0xFFFFFF;

        // create new user
        SystemAdminApiKey = await TeamClient.CreateSystemApiKeyAsync();
        HttpClient.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(SystemAdminApiKey.Authorization);

        // create default project
        Project = await ProjectsClient.CreateAsync();
        ProjectOwnerApiKey = await AddNewUser(Roles.ProjectOwner);
        await TeamClient.RemoveUserAsync(Project.ProjectId, Roles.ProjectOwner.RoleId, SystemAdminApiKey.UserId);
    }

    public Task<IPAddress> NewIpV4()
    {
        lock (_lastIp)
        {
            _lastIp = IPAddressUtil.Increment(_lastIp);
            return Task.FromResult(_lastIp);
        }
    }

    public async Task<IPAddress> NewIpV6()
    {
        return (await NewIpV4()).MapToIPv6();
    }

    public async Task<string> NewIpV4String() => (await NewIpV4()).ToString();
    public async Task<string> NewIpV6String() => (await NewIpV6()).ToString();

    public async Task<IPAddress> NewIpV4Db()
    {
        await Task.Delay(0);
        var address = new byte[4];
        new Random().NextBytes(address);
        return new IPAddress(address);
    }

    public async Task<IPEndPoint> NewEndPoint() => new(await NewIpV4(), 443);
    public async Task<IPEndPoint> NewEndPointIp6() => new(await NewIpV6(), 443);


    public async Task<AccessPoint> NewAccessPoint(IPEndPoint? ipEndPoint = null, AccessPointMode accessPointMode = AccessPointMode.PublicInToken,
        bool isListen = true, int udpPrt = 0)
    {
        ipEndPoint ??= await NewEndPoint();
        return new AccessPoint
        {
            UdpPort = udpPrt,
            IpAddress = ipEndPoint.Address.ToString(),
            TcpPort = ipEndPoint.Port,
            AccessPointMode = AccessPointMode.PublicInToken,
            IsListen = isListen
        };
    }

    public static async Task<TestInit> Create(Dictionary<string, string?>? appSettings = null,
        string environment = "Development")
    {
        appSettings ??= new Dictionary<string, string?>();
        var ret = new TestInit(appSettings, environment);
        await ret.Init();
        return ret;
    }

    public WebApplicationFactory<T> CreateWebApp<T>(Dictionary<string, string?> appSettings, string environment) where T : class
    {
        // Application
        var webApp = new WebApplicationFactory<T>()
            .WithWebHostBuilder(builder =>
            {
                foreach (var appSetting in appSettings)
                    builder.UseSetting(appSetting.Key, appSetting.Value);

                builder.UseEnvironment(environment);

                builder.ConfigureServices(services =>
                {
                    services.AddScoped<IAuthorizationProvider, TestAuthorizationProvider>();
                    services.AddSingleton<IHttpClientFactory>(this);
                });
            });
        return webApp;
    }

    public async Task<UserApiKey> AddNewUser(SimpleRole simpleRole, bool setAsCurrent = true)
    {
        var oldAuthorization = HttpClient.DefaultRequestHeaders.Authorization;
        HttpClient.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(SystemAdminApiKey.Authorization);

        var resourceId = simpleRole.IsRoot ? Guid.Empty : Project.ProjectId;
        var apiKey = await TeamClient.AddNewBotAsync(resourceId, simpleRole.RoleId, new TeamAddBotParam { Name = Guid.NewGuid().ToString() });

        HttpClient.DefaultRequestHeaders.Authorization = setAsCurrent
            ? AuthenticationHeaderValue.Parse(apiKey.Authorization) : oldAuthorization;

        return apiKey;
    }

    public static string NewEmail()
    {
        return $"{Guid.NewGuid()}@local";
    }

    public static ServerStatus NewServerStatus(string? configCode, bool randomStatus = false)
    {
        var rand = new Random();
        var ret = randomStatus
            ? new ServerStatus
            {
                SessionCount = rand.Next(1, 1000),
                AvailableMemory = rand.Next(150, 300) * 1000000000L,
                UsedMemory = rand.Next(0, 150) * 1000000000L,
                TcpConnectionCount = rand.Next(100, 500),
                UdpConnectionCount = rand.Next(501, 1000),
                ThreadCount = rand.Next(0, 50),
                ConfigCode = configCode,
                CpuUsage = rand.Next(0, 100),
                TunnelSpeed = new Traffic
                {
                    Sent = 1000000,
                    Received = 2000000
                }
            }
            : new ServerStatus
            {
                SessionCount = 0,
                AvailableMemory = 16 * 1000000000L,
                UsedMemory = 1 * 1000000000L,
                TcpConnectionCount = 0,
                UdpConnectionCount = 0,
                ThreadCount = 5,
                ConfigCode = configCode,
                CpuUsage = 25,
                TunnelSpeed = new Traffic()
            };

        return ret;
    }

    public async Task<ServerInfo> NewServerInfo(bool randomStatus = false)
    {
        var rand = new Random();
        var publicIp = await NewIpV6();
        var serverInfo = new ServerInfo(
            version: Version.Parse($"999.{rand.Next(0, 255)}.{rand.Next(0, 255)}.{rand.Next(0, 255)}"),
            environmentVersion: Environment.Version,
            privateIpAddresses: new[]
            {
                IPAddress.Parse($"192.168.{rand.Next(0, 255)}.{rand.Next(0, 255)}"),
                IPAddress.Parse($"192.168.{rand.Next(0, 255)}.{rand.Next(0, 255)}"),
                publicIp,
            },
            publicIpAddresses: new[]
            {
                await NewIpV4(),
                await NewIpV6(),
                publicIp,
            },
            status: NewServerStatus(null, randomStatus))
        {
            MachineName = $"MachineName-{Guid.NewGuid()}",
            OsInfo = $"{Environment.OSVersion.Platform}-{Guid.NewGuid()}",
            LogicalCoreCount = 2,
            TotalMemory = 20000000,
        };

        return serverInfo;
    }

    public async Task<SessionRequestEx> CreateSessionRequestEx(AccessToken accessToken, IPEndPoint hostEndPoint, Guid? clientId = null, IPAddress? clientIp = null)
    {
        var rand = new Random();

        var clientInfo = new ClientInfo
        {
            ClientId = clientId ?? Guid.NewGuid(),
            ClientVersion = $"999.{rand.Next(0, 999)}.{rand.Next(0, 999)}",
            UserAgent = "agent"
        };

        var accessKey = await AccessTokensClient.GetAccessKeyAsync(accessToken.ProjectId, accessToken.AccessTokenId);
        var vhToken = Token.FromAccessKey(accessKey);

        var secret = vhToken.Secret;
        var sessionRequestEx = new SessionRequestEx(
            accessToken.AccessTokenId,
            clientInfo,
            VhUtil.EncryptClientId(clientInfo.ClientId, secret),
            hostEndPoint)
        {
            ClientIp = clientIp ?? NewIpV4().Result
        };

        return sessionRequestEx;
    }

    public AgentClient CreateAgentClient(Guid serverId)
    {
        var installManual = ServersClient.GetInstallManualAsync(ProjectId, serverId).Result;

        var http = AgentApp.CreateClient();
        var options = new Server.Providers.HttpAccessServerProvider.HttpAccessServerOptions(
            installManual.AppSettings.HttpAccessServer.BaseUrl,
            installManual.AppSettings.HttpAccessServer.Authorization
        );

        return new AgentClient(http, options);
    }

    public async Task Sync(bool flushCache = true)
    {
        if (flushCache)
            await FlushCache();

        var oldAuthorization = HttpClient.DefaultRequestHeaders.Authorization;
        HttpClient.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(SystemAdminApiKey.Authorization);
        await SystemClient.SyncAsync();
        HttpClient.DefaultRequestHeaders.Authorization = oldAuthorization;
    }

    public async Task FlushCache()
    {
        await AgentCacheClient.Flush();
    }

    public HttpClient CreateClient(string name)
    {
        // this for simulating Agent HTTP
        if (name == AppOptions.AgentHttpClientName)
        {
            var claimIdentity = new ClaimsIdentity();
            claimIdentity.AddClaim(new Claim("usage_type", "system"));
            claimIdentity.AddClaim(new Claim(JwtRegisteredClaimNames.Sub, "test@local"));
            claimIdentity.AddClaim(new Claim(JwtRegisteredClaimNames.Email, "test@local"));
            claimIdentity.AddClaim(new Claim(ClaimTypes.Role, "System"));
            var scope = AgentApp.Services.CreateScope();
            var authenticationTokenBuilder = scope.ServiceProvider.GetRequiredService<BotAuthenticationTokenBuilder>();
            var authorization = authenticationTokenBuilder.CreateAuthenticationHeader(claimIdentity).Result;

            var httpClient = AgentApp.CreateClient();
            httpClient.BaseAddress = AppOptions.AgentUrl;
            httpClient.DefaultRequestHeaders.Authorization = authorization;
            return httpClient;
        }

        return WebApp.CreateClient();
    }

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
    }

    public void Dispose()
    {
        Scope.Dispose();
        HttpClient.Dispose();
    }
}