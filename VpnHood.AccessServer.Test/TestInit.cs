using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Authorization;
using VpnHood.AccessServer.Caching;
using VpnHood.AccessServer.Security;
using VpnHood.Common;
using VpnHood.Common.Net;
using User = VpnHood.AccessServer.Models.User;
using VhContext = VpnHood.AccessServer.Models.VhContext;
using Setting = VpnHood.AccessServer.Models.Setting;

namespace VpnHood.AccessServer.Test;

[TestClass]
public class TestInit : IDisposable
{
    public WebApplicationFactory<Program> WebApp { get; }
    public IServiceScope Scope { get; }
    public HttpClient Http { get; }
    public AppOptions AppOptions => WebApp.Services.GetRequiredService<IOptions<AppOptions>>().Value;
    public SystemCache SystemCache => WebApp.Services.GetRequiredService<SystemCache>();

    public AccessPointGroupController ServerFarmController => new(Http);
    public ServerController ServerController => new(Http);
    public AccessTokenController AccessTokenController => new(Http);
    public AccessPointGroupController AccessPointGroupController => new(Http);
    public AccessPointController AccessPointController => new(Http);
    public ProjectController ProjectController => new(Http);
    public AgentController AgentController2 { get; private set; } = default!;
    public AgentController AgentController1 { get; private set; } = default!;

    public User UserSystemAdmin1 { get; } = NewUser("Administrator1");
    public User UserProjectOwner1 { get; } = NewUser("Project Owner 1");
    public User User1 { get; } = NewUser("User1");
    public User User2 { get; } = NewUser("User2");
    public Guid ProjectId { get; private set; }
    public Guid ServerId1 { get; private set; }
    public Guid ServerId2 { get; private set; }
    public string PublicServerDns { get; } = $"publicfoo.{Guid.NewGuid()}.com";
    public string PrivateServerDns { get; } = $"privatefoo.{Guid.NewGuid()}.com";
    public IPEndPoint HostEndPointG1S1 { get; private set; } = null!; //in token
    public IPEndPoint HostEndPointG1S2 { get; private set; } = null!;
    public IPEndPoint HostEndPointG2S1 { get; private set; } = null!; //in token
    public IPEndPoint HostEndPointG2S2 { get; private set; } = null!;
    public IPAddress ClientIp1 { get; private set; } = null!;
    public IPAddress ClientIp2 { get; private set; } = null!;
    public AccessToken AccessToken1 { get; private set; } = null!;
    public Guid AccessPointGroupId1 { get; private set; }
    public Guid AccessPointGroupId2 { get; private set; }
    public ServerInfo ServerInfo1 { get; private set; } = default!;
    public ServerInfo ServerInfo2 { get; private set; } = default!;
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
        await using var scope = WebApp.Services.CreateAsyncScope();
        await using var vhContext = scope.ServiceProvider.GetRequiredService<VhContext>();

        var setting = await vhContext.Settings.FirstOrDefaultAsync();
        if (setting == null)
        {
            setting = new Setting
            {
                SettingId = 1,
                Reserved1 = "1000"
            };
            await vhContext.Settings.AddAsync(setting);
        }
        else
        {
            setting.Reserved1 = (long.Parse(setting.Reserved1 ?? "0") + 1).ToString();
            vhContext.Settings.Update(setting);
        }

        await vhContext.SaveChangesAsync();
        return new IPAddress(long.Parse(setting.Reserved1));
    }

    public async Task<IPEndPoint> NewEndPoint() => new(await NewIpV4(), 443);
    public async Task<IPEndPoint> NewEndPointIp6() => new(await NewIpV6(), 443);

    public static User NewUser(string name)
    {
        var userId = Guid.NewGuid();
        return new User
        {
            UserId = userId,
            AuthUserId = userId.ToString(),
            Email = $"{userId}@vpnhood.com",
            UserName = $"{name}_{userId}",
            MaxProjectCount = QuotaConstants.ProjectCount,
            CreatedTime = DateTime.UtcNow
        };
    }

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
    }

    private TestInit()
    {
        WebApp = CreateWebApp();
        Scope = WebApp.Services.CreateScope();
        Http = WebApp.CreateClient();
        SetHttpUser(UserSystemAdmin1.Email!);
    }
    public void SetHttpUser(string email)
    {
        Http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(JwtBearerDefaults.AuthenticationScheme,
                CreateUserAuthenticationCode(email));
    }

    public static async Task<TestInit> Create(bool useSharedProject = false)
    {
        var ret = new TestInit();
        await ret.Init(useSharedProject);
        return ret;
    }

    public static WebApplicationFactory<Program> CreateWebApp()
    {
        // Application
        var webApp = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(_ =>
                {
                });
            });
        webApp.Services.GetRequiredService<ServerManager>().AllowRedirect = false;
        return webApp;
    }

    private static async Task AddUser(VhContext vhContext, User user)
    {
        await vhContext.Users.AddAsync(user);
        var secureObject = await vhContext.AuthManager.CreateSecureObject(user.UserId, SecureObjectTypes.User);
        await vhContext.AuthManager.SecureObject_AddUserPermission(secureObject, user.UserId, PermissionGroups.UserBasic, user.UserId);
    }

    public async Task Init(bool useSharedProject = false)
    {
        QuotaConstants.ProjectCount = 0xFFFFFF;
        QuotaConstants.ServerCount = 0xFFFFFF;
        QuotaConstants.CertificateCount = 0xFFFFFF;
        QuotaConstants.AccessTokenCount = 0xFFFFFF;
        QuotaConstants.AccessPointCount = 0xFFFFFF;
        QuotaConstants.AccessPointGroupCount = 0xFFFFFF;

        HostEndPointG1S1 = await NewEndPoint();
        HostEndPointG1S2 = await NewEndPoint();
        HostEndPointG2S1 = await NewEndPoint();
        HostEndPointG2S2 = await NewEndPoint();
        ClientIp1 = await NewIpV4();
        ClientIp2 = await NewIpV4();
        ServerInfo1 = await NewServerInfo();
        ServerInfo2 = await NewServerInfo();

        await using var scope = WebApp.Services.CreateAsyncScope();
        await using var vhContext = scope.ServiceProvider.GetRequiredService<VhContext>();

        await AddUser(vhContext, UserSystemAdmin1);
        await AddUser(vhContext, UserProjectOwner1);
        await AddUser(vhContext, User1);
        await AddUser(vhContext, User2);
        await vhContext.AuthManager.Role_AddUser(AuthManager.SystemAdminRoleId, UserSystemAdmin1.UserId, AuthManager.SystemUserId);
        await vhContext.SaveChangesAsync();

        var projectController = new ProjectController(Http);
        var certificateController = new CertificateController(Http);
        var accessPointGroupController = new AccessPointGroupController(Http);

        // create default project
        Project? project;
        if (useSharedProject)
        {
            var sharedProjectId = Guid.Parse("648B9968-7221-4463-B70A-00A10919AE69");
            try
            {
                project = await projectController.ProjectsGetAsync(sharedProjectId);

                // add new owner to shared project
                var ownerRole = (await vhContext.AuthManager.SecureObject_GetRolePermissionGroups(project.ProjectId))
                    .Single(x => x.PermissionGroupId == PermissionGroups.ProjectOwner.PermissionGroupId);
                await vhContext.AuthManager.Role_AddUser(ownerRole.RoleId, UserProjectOwner1.UserId, AuthManager.SystemUserId);
                await vhContext.SaveChangesAsync();

            }
            catch
            {
                project = await projectController.ProjectsPostAsync(sharedProjectId);
            }
        }
        else
        {
            project = await projectController.ProjectsPostAsync();
        }

        // create Project1
        ProjectId = project.ProjectId;

        var certificate1 = await certificateController.CertificatesPostAsync(ProjectId, new CertificateCreateParams { SubjectName = $"CN={PublicServerDns}" });
        AccessPointGroupId1 = (await accessPointGroupController.AccessPointGroupsPostAsync(ProjectId, new AccessPointGroupCreateParams { CertificateId = certificate1.CertificateId })).AccessPointGroupId;

        var certificate2 = await certificateController.CertificatesPostAsync(ProjectId, new CertificateCreateParams { SubjectName = $"CN={PrivateServerDns}" });
        AccessPointGroupId2 = (await accessPointGroupController.AccessPointGroupsPostAsync(ProjectId, new AccessPointGroupCreateParams { CertificateId = certificate2.CertificateId })).AccessPointGroupId;

        var serverController = new ServerController(Http);
        var server1 = await serverController.ServersPostAsync(project.ProjectId, new ServerCreateParams());
        var server2 = await serverController.ServersPostAsync(project.ProjectId, new ServerCreateParams());
        ServerId1 = server1.ServerId;
        ServerId2 = server2.ServerId;
        await InitAccessPoint(server1, HostEndPointG1S1, AccessPointGroupId1, AccessPointMode.PublicInToken);
        await InitAccessPoint(server1, HostEndPointG2S1, AccessPointGroupId2, AccessPointMode.Public);
        await InitAccessPoint(server2, HostEndPointG1S2, AccessPointGroupId1, AccessPointMode.Public);
        await InitAccessPoint(server2, HostEndPointG2S2, AccessPointGroupId2, AccessPointMode.PublicInToken);

        // configure servers
        AgentController1 = await CreateAgentController(server1.ServerId, ServerInfo1);
        AgentController2 = await CreateAgentController(server2.ServerId, ServerInfo2);

        // Create AccessToken1
        var accessTokenControl = new AccessTokenController(Http);
        AccessToken1 = await accessTokenControl.AccessTokensPostAsync(ProjectId,
            new AccessTokenCreateParams
            {
                Secret = Util.GenerateSessionKey(),
                AccessTokenName = $"Access1_{Guid.NewGuid()}",
                AccessPointGroupId = AccessPointGroupId1
            });
    }

    /// <summary>
    /// AccessToken1 (public)
    ///     // Client1 => 1 session => 2 ItemUsageInfo
    ///     // Client2 => 1 session => 2 ItemUsageInfo
    /// AccessToken2 (public) 
    ///     // Client3 => 1 session => 2 ItemUsageInfo
    ///     // Client4 => 1 session => 2 ItemUsageInfo
    /// AccessToken3 (private) 
    ///     // Client5 => 1 session => 2 ItemUsageInfo
    ///     // Client6 => 1 session => 2 ItemUsageInfo
    /// </summary>
    public async Task<TestFillData> Fill()
    {
        var fillData = new TestFillData();
        var agentController = CreateAgentController();
        var accessTokenControl = new AccessTokenController(Http);

        // ----------------
        // Create accessToken1 public
        // ----------------
        var accessToken = await accessTokenControl.AccessTokensPostAsync(ProjectId,
            new AccessTokenCreateParams
            {
                Secret = Util.GenerateSessionKey(),
                AccessTokenName = $"Access1_{Guid.NewGuid()}",
                AccessPointGroupId = AccessPointGroupId2,
                IsPublic = true
            });
        fillData.AccessTokens.Add(accessToken);

        // accessToken1 - sessions1
        var sessionRequestEx = CreateSessionRequestEx(accessToken, hostEndPoint: HostEndPointG2S1);
        var sessionResponseEx = await agentController.SessionsPostAsync(sessionRequestEx);
        await agentController.UsageAsync(sessionResponseEx.SessionId, false, fillData.ItemUsageInfo);
        await agentController.UsageAsync(sessionResponseEx.SessionId, false, fillData.ItemUsageInfo);
        fillData.SessionResponses.Add(sessionResponseEx);
        fillData.SessionRequests.Add(sessionRequestEx);

        // accessToken1 - sessions2
        sessionRequestEx = CreateSessionRequestEx(accessToken, hostEndPoint: HostEndPointG2S1);
        sessionResponseEx = await agentController.SessionsPostAsync(sessionRequestEx);
        await agentController.UsageAsync(sessionResponseEx.SessionId, false, fillData.ItemUsageInfo);
        await agentController.UsageAsync(sessionResponseEx.SessionId, false, fillData.ItemUsageInfo);
        fillData.SessionResponses.Add(sessionResponseEx);
        fillData.SessionRequests.Add(sessionRequestEx);

        // ----------------
        // Create accessToken2 public
        // ----------------
        accessToken = await accessTokenControl.AccessTokensPostAsync(ProjectId,
            new AccessTokenCreateParams
            {
                Secret = Util.GenerateSessionKey(),
                AccessTokenName = $"Access2_{Guid.NewGuid()}",
                AccessPointGroupId = AccessPointGroupId1,
                IsPublic = true
            });
        fillData.AccessTokens.Add(accessToken);

        // accessToken2 - sessions1
        sessionRequestEx = CreateSessionRequestEx(accessToken);
        sessionResponseEx = await agentController.SessionsPostAsync(sessionRequestEx);
        await agentController.UsageAsync(sessionResponseEx.SessionId, closeSession: false, fillData.ItemUsageInfo);
        await agentController.UsageAsync(sessionResponseEx.SessionId, closeSession: false, fillData.ItemUsageInfo);
        fillData.SessionResponses.Add(sessionResponseEx);
        fillData.SessionRequests.Add(sessionRequestEx);

        // accessToken2 - sessions2
        sessionRequestEx = CreateSessionRequestEx(accessToken);
        sessionResponseEx = await agentController.SessionsPostAsync(sessionRequestEx);
        await agentController.UsageAsync(sessionResponseEx.SessionId, closeSession: false, fillData.ItemUsageInfo);
        await agentController.UsageAsync(sessionResponseEx.SessionId, closeSession: false, fillData.ItemUsageInfo);
        fillData.SessionResponses.Add(sessionResponseEx);
        fillData.SessionRequests.Add(sessionRequestEx);

        // ----------------
        // Create accessToken3 private
        // ----------------
        accessToken = await accessTokenControl.AccessTokensPostAsync(ProjectId,
            new AccessTokenCreateParams
            {
                Secret = Util.GenerateSessionKey(),
                AccessTokenName = $"Access3_{Guid.NewGuid()}",
                AccessPointGroupId = AccessPointGroupId1,
                IsPublic = false
            });
        fillData.AccessTokens.Add(accessToken);

        // accessToken3 - sessions1
        sessionRequestEx = CreateSessionRequestEx(accessToken);
        sessionResponseEx = await agentController.SessionsPostAsync(sessionRequestEx);
        await agentController.UsageAsync(sessionResponseEx.SessionId, closeSession: false, fillData.ItemUsageInfo);
        await agentController.UsageAsync(sessionResponseEx.SessionId, closeSession: false, fillData.ItemUsageInfo);
        fillData.SessionResponses.Add(sessionResponseEx);
        fillData.SessionRequests.Add(sessionRequestEx);

        // accessToken3 - sessions2
        // actualAccessCount++; it is private!
        sessionRequestEx = CreateSessionRequestEx(accessToken);
        sessionResponseEx = await agentController.SessionsPostAsync(sessionRequestEx);
        await agentController.UsageAsync(sessionResponseEx.SessionId, closeSession: false, fillData.ItemUsageInfo);
        await agentController.UsageAsync(sessionResponseEx.SessionId, closeSession: false, fillData.ItemUsageInfo);
        fillData.SessionResponses.Add(sessionResponseEx);
        fillData.SessionRequests.Add(sessionRequestEx);

        return fillData;
    }

    private async Task InitAccessPoint(Api.Server server,
        IPEndPoint hostEndPoint,
        Guid accessPointGroupId,
        AccessPointMode accessPointMode, bool isListen = true)
    {
        // create server accessPoints
        var accessPointController = new AccessPointController(Http);
        await accessPointController.AccessPointsPostAsync(ProjectId,
            new AccessPointCreateParams
            {
                ServerId = server.ServerId,
                IpAddress = hostEndPoint.Address.ToString(),
                AccessPointGroupId = accessPointGroupId,
                TcpPort = hostEndPoint.Port,
                IsListen = isListen,
                AccessPointMode = accessPointMode,
            }
        );
    }

    public static ServerStatus NewServerStatus(string? configCode)
    {
        var rand = new Random();
        return new ServerStatus
        {
            SessionCount = rand.Next(1, 1000),
            FreeMemory = rand.Next(150, 300) * 1000000000L,
            UsedMemory = rand.Next(0, 150) * 1000000000L,
            TcpConnectionCount = rand.Next(100, 500),
            UdpConnectionCount = rand.Next(501, 1000),
            ThreadCount = rand.Next(0, 50),
            ConfigCode = configCode
        };
    }

    public async Task<ServerInfo> NewServerInfo()
    {
        var rand = new Random();
        var publicIp = await NewIpV6();
        var serverInfo = new ServerInfo
        {
            Version = Version.Parse($"999.{rand.Next(0, 255)}.{rand.Next(0, 255)}.{rand.Next(0, 255)}").ToString(),
            EnvironmentVersion = Environment.Version.ToString(),
            PrivateIpAddresses = new[]
            {
                IPAddress.Parse($"192.168.{rand.Next(0, 255)}.{rand.Next(0, 255)}").ToString(),
                IPAddress.Parse($"192.168.{rand.Next(0, 255)}.{rand.Next(0, 255)}").ToString(),
                publicIp.ToString(),
            },
            PublicIpAddresses = new[]
            {
                (await NewIpV4()).ToString(),
                (await NewIpV6()).ToString(),
                publicIp.ToString(),
            },
            Status = NewServerStatus(null),
            MachineName = $"MachineName-{Guid.NewGuid()}",
            OsInfo = $"{Environment.OSVersion.Platform}-{Guid.NewGuid()}"
        };

        return serverInfo;
    }

    public SessionRequestEx CreateSessionRequestEx(AccessToken accessToken, Guid? clientId = null, IPEndPoint? hostEndPoint = null, IPAddress? clientIp = null)
    {
        var rand = new Random();

        var clientInfo = new ClientInfo
        {
            ClientId = clientId ?? Guid.NewGuid(),
            ClientVersion = $"999.{rand.Next(0, 999)}.{rand.Next(0, 999)}",
            UserAgent = "agent"
        };

        var sessionRequestEx = new SessionRequestEx
        {
            TokenId = accessToken.AccessTokenId,
            ClientInfo = clientInfo,
            EncryptedClientId = Util.EncryptClientId(clientInfo.ClientId, accessToken.Secret),
            HostEndPoint = hostEndPoint?.ToString() ?? HostEndPointG1S1.ToString(),
            ClientIp = clientIp?.ToString() ?? NewIpV4().Result.ToString()
        };

        return sessionRequestEx;
    }

    public AgentController CreateAgentController(Guid? serverId = null)
    {
        serverId ??= ServerId1;

        using var scope = WebApp.Services.CreateAsyncScope();
        using var vhContext = scope.ServiceProvider.GetRequiredService<VhContext>();
        var server = vhContext.Servers.Single(x => x.ServerId == serverId);
        var appOptions = scope.ServiceProvider.GetRequiredService<IOptions<AppOptions>>().Value;

        var claims = new List<Claim>
        {
            new("authorization_code", server.AuthorizationCode.ToString()),
            new("usage_type", "agent"),
        };

        // create jwt
        var jwt = JwtTool.CreateSymmetricJwt(appOptions.AuthenticationKey,
            AppOptions.AuthIssuer, AppOptions.AuthAudience, serverId.ToString()!, claims.ToArray());

        var http = WebApp.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(JwtBearerDefaults.AuthenticationScheme, jwt);
        return new AgentController(http);
    }

    public async Task<AgentController> CreateAgentController(Guid? serverId, ServerInfo? serverInfo)
    {
        var agentController = CreateAgentController(serverId);
        serverInfo ??= await NewServerInfo();
        await ConfigAgent(agentController, serverInfo);
        return agentController;
    }

    public async Task<ServerInfo> ConfigAgent(AgentController agentController, ServerInfo? serverInfo = null)
    {
        serverInfo ??= await NewServerInfo();
        var serverConfig = await agentController.ConfigureAsync(serverInfo);
        serverInfo.Status.ConfigCode = serverConfig.ConfigCode;
        await agentController.StatusAsync(serverInfo.Status);
        return serverInfo;
    }


    public async Task Sync()
    {
        var syncManager = WebApp.Services.GetRequiredService<SyncManager>();
        await syncManager.Sync();
    }

    public async Task FlushCache()
    {
        await using var scope = WebApp.Services.CreateAsyncScope();
        await using var vhContext = scope.ServiceProvider.GetRequiredService<VhContext>();
        var systemCache = WebApp.Services.GetRequiredService<SystemCache>();
        await systemCache.SaveChanges(vhContext);
    }

    public string CreateUserAuthenticationCode(string email)
    {
        using var scope = WebApp.Services.CreateAsyncScope();
        var appOptions = scope.ServiceProvider.GetRequiredService<IOptions<AppOptions>>().Value;

        var claims = new List<Claim>
            {
                new("emails", email),
                new("usage_type", "api_caller"),
            };

        // create jwt
        var jwt = JwtTool.CreateSymmetricJwt(appOptions.AuthenticationKey,
            AppOptions.AuthIssuer, AppOptions.AuthAudience, $"userid-{email}", claims.ToArray());

        return jwt;
    }

    public void Dispose()
    {
        Scope.Dispose();
        Http.Dispose();
    }

    public Task<SampleFarm> CreateSampleFarm()
    {
        return SampleFarm.Create(this);
    }
}