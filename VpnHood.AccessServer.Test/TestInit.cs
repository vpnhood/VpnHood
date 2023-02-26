using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Agent;
using VpnHood.AccessServer.Agent.Services;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Clients;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Server;
using VpnHood.Server.Messaging;
using UserModel = VpnHood.AccessServer.Models.UserModel;

namespace VpnHood.AccessServer.Test;

[TestClass]
public class TestInit : IDisposable, IHttpClientFactory
{
    public WebApplicationFactory<Program> WebApp { get; }
    public WebApplicationFactory<Agent.Program> AgentApp { get; }

    public IServiceScope Scope { get; }
    public IServiceScope AgentScope { get; }
    public VhContext VhContext => Scope.ServiceProvider.GetRequiredService<VhContext>();
    public VhReportContext VhReportContext => Scope.ServiceProvider.GetRequiredService<VhReportContext>();
    public CacheService CacheService => AgentScope.ServiceProvider.GetRequiredService<CacheService>();
    public HttpClient Http { get; }
    public AgentOptions AgentOptions => AgentApp.Services.GetRequiredService<IOptions<AgentOptions>>().Value;
    public AppOptions AppOptions => WebApp.Services.GetRequiredService<IOptions<AppOptions>>().Value;
    public AgentCacheClient AgentCacheClient => Scope.ServiceProvider.GetRequiredService<AgentCacheClient>();
    public AgentSystemClient AgentSystemClient => Scope.ServiceProvider.GetRequiredService<AgentSystemClient>();
    public AccessPointGroupsClient AccessPointGroupsClient => new(Http);
    public ServersClient ServersClient => new(Http);
    public CertificatesClient CertificatesClient => new(Http);
    public AccessTokensClient AccessTokensClient => new(Http);
    public ProjectsClient ProjectsClient => new(Http);
    public IpLocksClient IpLocksClient => new(Http);
    public AccessesClient AccessesClient => new(Http);
    public DevicesClient DevicesClient => new(Http);
    public SystemClient SystemClient => new(Http);
    public ServerProfilesClient ServerProfilesClient => new(Http);

    public UserModel UserSystemAdmin1 { get; } = NewUser("Administrator1");
    public UserModel UserProjectOwner1 { get; } = NewUser("Project Owner 1");
    public UserModel User1 { get; } = NewUser("User1");
    public UserModel User2 { get; } = NewUser("User2");
    public Guid ProjectId { get; private set; }
    public DateTime CreatedTime { get; } = DateTime.UtcNow;

    private static IPAddress _lastIp = IPAddress.Parse("1.0.0.0");

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

    public static UserModel NewUser(string name)
    {
        var userId = Guid.NewGuid();
        return new UserModel
        {
            UserId = userId,
            AuthUserId = userId.ToString(),
            Email = $"{userId}@vpnhood.com",
            UserName = $"{name}_{userId}",
            MaxProjectCount = QuotaConstants.ProjectCount,
            AuthCode = Guid.NewGuid().ToString(),
            CreatedTime = DateTime.UtcNow
        };
    }

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

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
    }

    private TestInit(Dictionary<string, string?> appSettings, string environment)
    {
        Environment.SetEnvironmentVariable("IsTest", true.ToString());
        WebApp = CreateWebApp<Program>(appSettings, environment);
        AgentApp = CreateWebApp<Agent.Program>(appSettings, environment);
        AgentOptions.AllowRedirect = false;
        Scope = WebApp.Services.CreateScope();
        AgentScope = AgentApp.Services.CreateScope();
        Http = WebApp.CreateClient();
    }

    public async Task SetHttpUser(string email, Claim[]? claims = null)
    {
        var claimsIdentity = new ClaimsIdentity();
        claimsIdentity.AddClaim(new Claim(JwtRegisteredClaimNames.Sub, email));
        claimsIdentity.AddClaim(new Claim(JwtRegisteredClaimNames.Email, email));
        if (claims != null)
            claimsIdentity.AddClaims(claims);

        var authenticationTokenBuilder = Scope.ServiceProvider.GetRequiredService<BotAuthenticationTokenBuilder>();
        Http.DefaultRequestHeaders.Authorization = await authenticationTokenBuilder.CreateAuthenticationHeader(claimsIdentity);
    }

    public static async Task<TestInit> Create(bool useSharedProject = false, Dictionary<string, string?>? appSettings = null, string environment = "Development",
        bool createServers = true)
    {
        appSettings ??= new Dictionary<string, string?>();
        var ret = new TestInit(appSettings, environment);
        await ret.Init(useSharedProject, createServers);
        return ret;
    }

    public HttpClient CreateClient(string name)
    {
        if (name == AppOptions.AgentHttpClientName)
        {
            var scope = AgentApp.Services.CreateScope();
            var authenticationTokenBuilder = scope.ServiceProvider.GetRequiredService<BotAuthenticationTokenBuilder>();
            var claimIdentity = new ClaimsIdentity();
            claimIdentity.AddClaim(new Claim("usage_type", "system"));
            claimIdentity.AddClaim(new Claim(JwtRegisteredClaimNames.Email, "test@local"));
            claimIdentity.AddClaim(new Claim(ClaimTypes.Role, "System"));
            var authorization = authenticationTokenBuilder.CreateAuthenticationHeader(claimIdentity).Result;

            var httpClient = AgentApp.CreateClient();
            httpClient.BaseAddress = AppOptions.AgentUrl;
            httpClient.DefaultRequestHeaders.Authorization = authorization;
            return httpClient;
        }

        return WebApp.CreateClient();
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
                    services.AddSingleton<IHttpClientFactory>(this);
                });
            });
        return webApp;
    }

    private static async Task AddUser(VhContext vhContext, MultilevelAuthService multilevelAuthService, UserModel user)
    {
        await vhContext.Users.AddAsync(user);
        var secureObject = await multilevelAuthService.CreateSecureObject(user.UserId, SecureObjectTypes.User);
        await multilevelAuthService.SecureObject_AddUserPermission(secureObject, user.UserId, PermissionGroups.UserBasic, user.UserId);
    }

    public async Task Init(bool useSharedProject = false, bool createServers = false)
    {
        QuotaConstants.ProjectCount = 0xFFFFFF;
        QuotaConstants.ServerCount = 0xFFFFFF;
        QuotaConstants.CertificateCount = 0xFFFFFF;
        QuotaConstants.AccessTokenCount = 0xFFFFFF;
        QuotaConstants.AccessPointCount = 0xFFFFFF;
        QuotaConstants.AccessPointGroupCount = 0xFFFFFF;

        await using var scope = WebApp.Services.CreateAsyncScope();
        var vhContext = scope.ServiceProvider.GetRequiredService<VhContext>();
        var multilevelAuthRepo = scope.ServiceProvider.GetRequiredService<MultilevelAuthService>();

        await AddUser(vhContext, multilevelAuthRepo, UserSystemAdmin1);
        await AddUser(vhContext, multilevelAuthRepo, UserProjectOwner1);
        await AddUser(vhContext, multilevelAuthRepo, User1);
        await AddUser(vhContext, multilevelAuthRepo, User2);
        await vhContext.SaveChangesAsync();
        await SetHttpUser(UserSystemAdmin1.Email!);

        await multilevelAuthRepo.Role_AddUser(MultilevelAuthService.SystemAdminRoleId, UserSystemAdmin1.UserId, MultilevelAuthService.SystemUserId);

        // create default project
        Project? project;
        if (useSharedProject)
        {
            var sharedProjectId = Guid.Parse("648B9968-7221-4463-B70A-00A10919AE69");
            try
            {
                project = await ProjectsClient.GetAsync(sharedProjectId);

                // add new owner to shared project
                var ownerRole = (await multilevelAuthRepo.SecureObject_GetRolePermissionGroups(project.ProjectId))
                    .Single(x => x.PermissionGroupId == PermissionGroups.ProjectOwner.PermissionGroupId);
                await multilevelAuthRepo.Role_AddUser(ownerRole.RoleId, UserProjectOwner1.UserId, MultilevelAuthService.SystemUserId);
            }
            catch
            {
                project = await ProjectsClient.CreateAsync(sharedProjectId);
            }
        }
        else
        {
            project = await ProjectsClient.CreateAsync();
        }

        // create Project1
        ProjectId = project.ProjectId;
    }

    public static ServerStatus NewServerStatus(string? configCode)
    {
        var rand = new Random();
        return new ServerStatus
        {
            SessionCount = rand.Next(1, 1000),
            AvailableMemory = rand.Next(150, 300) * 1000000000L,
            UsedMemory = rand.Next(0, 150) * 1000000000L,
            TcpConnectionCount = rand.Next(100, 500),
            UdpConnectionCount = rand.Next(501, 1000),
            ThreadCount = rand.Next(0, 50),
            ConfigCode = configCode,
            CpuUsage = 5,
            TunnelReceiveSpeed = 2000000,
            TunnelSendSpeed = 1000000
        };
    }

    public async Task<ServerInfo> NewServerInfo()
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
            status: NewServerStatus(null))
        {
            MachineName = $"MachineName-{Guid.NewGuid()}",
            OsInfo = $"{Environment.OSVersion.Platform}-{Guid.NewGuid()}",
            LogicalCoreCount = 2,
            TotalMemory = 20000000,
        };

        return serverInfo;
    }

    public SessionRequestEx CreateSessionRequestEx(AccessToken accessToken, IPEndPoint hostEndPoint, Guid? clientId = null, IPAddress? clientIp = null)
    {
        var rand = new Random();

        var clientInfo = new ClientInfo
        {
            ClientId = clientId ?? Guid.NewGuid(),
            ClientVersion = $"999.{rand.Next(0, 999)}.{rand.Next(0, 999)}",
            UserAgent = "agent"
        };

        var accessKey = AccessTokensClient.GetAccessKeyAsync(accessToken.ProjectId, accessToken.AccessTokenId).Result;
        var vhToken = Token.FromAccessKey(accessKey);

        var secret = vhToken.Secret;
        var sessionRequestEx = new SessionRequestEx(
            accessToken.AccessTokenId,
            clientInfo,
            Util.EncryptClientId(clientInfo.ClientId, secret),
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
        await SystemClient.SyncAsync();
    }

    public async Task FlushCache()
    {
        await AgentCacheClient.Flush();
    }

    public void Dispose()
    {
        Scope.Dispose();
        Http.Dispose();
    }
}