using System.Net;
using System.Net.Http.Headers;
using GrayMint.Authorization.Abstractions;
using GrayMint.Authorization.RoleManagement.RoleProviders.Dtos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.Options;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Providers.Acme;
using VpnHood.AccessServer.Providers.Hosts;
using VpnHood.AccessServer.Report.Persistence;
using VpnHood.AccessServer.Security;
using VpnHood.AccessServer.Test.Helper;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Server.Access;
using VpnHood.Server.Access.Messaging;
using ApiKey = VpnHood.AccessServer.Api.ApiKey;
using ClientInfo = VpnHood.Common.Messaging.ClientInfo;
using HttpAccessManagerOptions = VpnHood.Server.Access.Managers.Http.HttpAccessManagerOptions;
using Token = VpnHood.Common.Token;

namespace VpnHood.AccessServer.Test;

public class TestApp : IHttpClientFactory, IDisposable
{
    public WebApplicationFactory<Program> WebApp { get; }
    public AgentTestApp AgentTestApp { get; }

    public IServiceScope Scope { get; }
    public VhContext VhContext => Scope.ServiceProvider.GetRequiredService<VhContext>();
    public VhReportContext VhReportContext => Scope.ServiceProvider.GetRequiredService<VhReportContext>();
    public HttpClient HttpClient { get; }
    public AppOptions AppOptions => WebApp.Services.GetRequiredService<IOptions<AppOptions>>().Value;
    public ServerFarmsClient ServerFarmsClient => new(HttpClient);
    public ServersClient ServersClient => new(HttpClient);
    public ReportClient ReportClient => new(HttpClient);
    public AccessTokensClient AccessTokensClient => new(HttpClient);
    public ProjectsClient ProjectsClient => new(HttpClient);
    public IpLocksClient IpLocksClient => new(HttpClient);
    public AccessesClient AccessesClient => new(HttpClient);
    public DevicesClient DevicesClient => new(HttpClient);
    public SystemClient SystemClient => new(HttpClient);
    public ServerProfilesClient ServerProfilesClient => new(HttpClient);
    public HostOrdersClient HostOrdersClient => new(HttpClient);
    public TeamClient TeamClient => new(HttpClient);
    public TestHostProvider HostProvider => throw new NotImplementedException(); //todo
    public AgentCacheClient AgentCacheClient => Scope.ServiceProvider.GetRequiredService<AgentCacheClient>();
    public ILogger<TestApp> Logger => Scope.ServiceProvider.GetRequiredService<ILogger<TestApp>>();

    public ApiKey SystemAdminApiKey { get; private set; } = default!;

    public AuthenticationHeaderValue SystemAdminAuthorization =>
        new(SystemAdminApiKey.AccessToken.Scheme, SystemAdminApiKey.AccessToken.Value);

    public ApiKey ProjectOwnerApiKey { get; private set; } = default!;
    public Project Project { get; private set; } = default!;
    public Guid ProjectId => Project.ProjectId;
    public DateTime CreatedTime { get; } = DateTime.UtcNow;

    private static IPAddress _lastIp = IPAddress.Parse("1.0.0.0");

    private TestApp(Dictionary<string, string?> appSettings, string environment)
    {
        // AgentTestApp should not any dependency to the main app
        AgentTestApp = new AgentTestApp(appSettings, environment);

        // create main app
        WebApp = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => {
                builder.UseSetting("IsTest", "1");
                foreach (var appSetting in appSettings)
                    builder.UseSetting(appSetting.Key, appSetting.Value);

                builder.UseEnvironment(environment);
                builder.ConfigureServices(services => {
                    services.AddScoped<IAuthorizationProvider, TestAuthorizationProvider>();
                    services.AddSingleton<IHttpClientFactory>(this);
                    services.AddSingleton<IAcmeOrderFactory, TestAcmeOrderFactory>();
                    services.AddSingleton<IHostProviderFactory, TestHostProviderFactory>();
                });
            });

        Scope = WebApp.Services.CreateScope();
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
        SystemAdminApiKey = await TeamClient.CreateSystemApiKeyAsync("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
        HttpClient.DefaultRequestHeaders.Authorization = SystemAdminAuthorization;

        // create default project
        Project = await ProjectsClient.CreateAsync();
        ProjectOwnerApiKey = await AddNewUser(Roles.ProjectOwner);
        await TeamClient.RemoveUserAsync(Project.ProjectId.ToString(), Roles.ProjectOwner.RoleId,
            SystemAdminApiKey.UserId);
    }

    public Task<IPAddress> NewIpV4()
    {
        lock (_lastIp) {
            _lastIp = IPAddressUtil.Increment(_lastIp);
            return Task.FromResult(_lastIp);
        }
    }

    public async Task<IPAddress> NewIpV6()
    {
        return (await NewIpV4()).MapToIPv6();
    }

    public async Task<string> NewIpV4String() => (await NewIpV4()).ToString();

    // ReSharper disable once UnusedMember.Global
    public async Task<string> NewIpV6String() => (await NewIpV6()).ToString();

    // ReSharper disable once UnusedMember.Global
    public async Task<IPAddress> NewIpV4Db()
    {
        await Task.Delay(0);
        var address = new byte[4];
        new Random().NextBytes(address);
        return new IPAddress(address);
    }

    public async Task<IPEndPoint> NewEndPoint(int port = 443) => new(await NewIpV4(), port);

    // ReSharper disable once UnusedMember.Global
    public async Task<IPEndPoint> NewEndPointIp6(int port = 443) => new(await NewIpV6(), port);


    public async Task<AccessPoint> NewAccessPoint(IPEndPoint? ipEndPoint = null,
        AccessPointMode accessPointMode = AccessPointMode.PublicInToken,
        bool isListen = true, int? udpPort = null)
    {
        ipEndPoint ??= await NewEndPoint();
        return new AccessPoint {
            UdpPort = udpPort ?? ipEndPoint.Port,
            IpAddress = ipEndPoint.Address.ToString(),
            TcpPort = ipEndPoint.Port,
            AccessPointMode = accessPointMode,
            IsListen = isListen
        };
    }

    public static async Task<TestApp> Create(Dictionary<string, string?>? appSettings = null,
        string environment = "Development")
    {
        appSettings ??= new Dictionary<string, string?>();
        var ret = new TestApp(appSettings, environment);
        await ret.Init();
        return ret;
    }

    public WebApplicationFactory<T> CreateWebApp<T>(Dictionary<string, string?> appSettings, string environment)
        where T : class
    {
        // Application
        var webApp = new WebApplicationFactory<T>()
            .WithWebHostBuilder(builder => {
                foreach (var appSetting in appSettings)
                    builder.UseSetting(appSetting.Key, appSetting.Value);

                builder.UseEnvironment(environment);

                builder.ConfigureServices(services => {
                    services.AddScoped<IAuthorizationProvider, TestAuthorizationProvider>();
                    services.AddSingleton<IHttpClientFactory>(this);
                });
            });

        return webApp;
    }

    public async Task<ApiKey> AddNewUser(GmRole role, bool setAsCurrent = true)
    {
        var oldAuthorization = HttpClient.DefaultRequestHeaders.Authorization;
        HttpClient.DefaultRequestHeaders.Authorization = SystemAdminAuthorization;

        var resourceId = role.IsRoot ? Guid.Empty : Project.ProjectId;
        var apiKey = await TeamClient.AddNewBotAsync(resourceId.ToString(), role.RoleId,
            new TeamAddBotParam { Name = Guid.NewGuid().ToString() });

        HttpClient.DefaultRequestHeaders.Authorization = setAsCurrent
            ? new AuthenticationHeaderValue(apiKey.AccessToken.Scheme, apiKey.AccessToken.Value)
            : oldAuthorization;

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
            ? new ServerStatus {
                SessionCount = rand.Next(1, 1000),
                AvailableMemory = rand.Next(150, 300) * 1000000000L,
                UsedMemory = rand.Next(0, 150) * 1000000000L,
                TcpConnectionCount = rand.Next(100, 500),
                UdpConnectionCount = rand.Next(501, 1000),
                ThreadCount = rand.Next(0, 50),
                ConfigCode = configCode,
                CpuUsage = rand.Next(0, 100),
                TunnelSpeed = new Traffic {
                    Sent = 1000000,
                    Received = 2000000
                }
            }
            : new ServerStatus {
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

    public async Task<ServerInfo> NewServerInfo(bool randomStatus = false, int? logicalCore = null,
        IPAddress? publicIpV4 = null)
    {
        var rand = new Random();
        var publicIp = await NewIpV6();
        var serverInfo = new ServerInfo {
            Version = Version.Parse($"999.{rand.Next(0, 255)}.{rand.Next(0, 255)}.{rand.Next(0, 255)}"),
            EnvironmentVersion = Environment.Version,
            PrivateIpAddresses = [
                IPAddress.Parse($"192.168.{rand.Next(0, 255)}.{rand.Next(0, 255)}"),
                IPAddress.Parse($"192.168.{rand.Next(0, 255)}.{rand.Next(0, 255)}"),
                publicIp
            ],
            PublicIpAddresses = [
                publicIpV4 ?? await NewIpV4(),
                await NewIpV6(),
                publicIp
            ],
            Status = NewServerStatus(null, randomStatus),
            MachineName = $"MachineName-{Guid.NewGuid()}",
            OsInfo = $"{Environment.OSVersion.Platform}-{Guid.NewGuid()}",
            LogicalCoreCount = logicalCore ?? 2,
            TotalMemory = 20000000,
            FreeUdpPortV4 = new Random().Next(2000, 9000),
            FreeUdpPortV6 = new Random().Next(2000, 9000)
        };

        return serverInfo;
    }

    public async Task<SessionRequestEx> CreateSessionRequestEx(AccessToken accessToken, IPEndPoint hostEndPoint,
        Guid? clientId = null, IPAddress? clientIp = null
        , string? extraData = null, string? locationPath = null, bool allowRedirect = false,
        ClientInfo? clientInfo = null)
    {
        var rand = new Random();
        if (clientInfo != null && clientId != null)
            throw new InvalidOperationException(
                "Could not set both clientInfo & clientId parameters at the same time.");

        clientInfo ??= new ClientInfo {
            ClientId = clientId ?? Guid.NewGuid(),
            ClientVersion = $"999.{rand.Next(0, 999)}.{rand.Next(0, 999)}",
            UserAgent = "agent",
            ProtocolVersion = 0
        };

        var accessKey = await AccessTokensClient.GetAccessKeyAsync(accessToken.ProjectId, accessToken.AccessTokenId);
        var vhToken = Token.FromAccessKey(accessKey);

        var secret = vhToken.Secret;
        var sessionRequestEx = new SessionRequestEx {
            ClientInfo = clientInfo,
            TokenId = accessToken.AccessTokenId.ToString(),
            EncryptedClientId = VhUtil.EncryptClientId(clientInfo.ClientId, secret),
            ClientIp = clientIp ?? NewIpV4().Result,
            HostEndPoint = hostEndPoint,
            ExtraData = extraData ?? Guid.NewGuid().ToString(),
            ServerLocation = locationPath,
            AllowRedirect = allowRedirect
        };

        return sessionRequestEx;
    }

    public AgentClient CreateAgentClient(Guid serverId)
    {
        var installManual = ServersClient.GetInstallManualAsync(ProjectId, serverId).Result;

        var options = new HttpAccessManagerOptions(
            installManual.AppSettings.HttpAccessManager.BaseUrl,
            installManual.AppSettings.HttpAccessManager.Authorization);

        return new AgentClient(AgentTestApp.HttpClient, options);
    }

    public async Task Sync(bool flushCache = true)
    {
        if (flushCache)
            await FlushCache();

        var oldAuthorization = HttpClient.DefaultRequestHeaders.Authorization;
        HttpClient.DefaultRequestHeaders.Authorization = SystemAdminAuthorization;
        await SystemClient.SyncAsync();
        HttpClient.DefaultRequestHeaders.Authorization = oldAuthorization;
    }

    public Task FlushCache()
    {
        return AgentCacheClient.Flush();
    }

    // IHttpClientFactory.CreateClient
    public HttpClient CreateClient(string name)
    {
        // this for simulating Agent HTTP
        return name == AppOptions.AgentHttpClientName
            ? AgentTestApp.HttpClient
            : WebApp.CreateClient();
    }

    public void Dispose()
    {
        Scope.Dispose();
        HttpClient.Dispose();
        AgentTestApp.Dispose();
        WebApp.Dispose();
    }
}