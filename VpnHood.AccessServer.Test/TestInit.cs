using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Authorization;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Server;
using VpnHood.Server.Messaging;

namespace VpnHood.AccessServer.Test;

[TestClass]
public class TestInit : IDisposable
{
    public WebApplicationFactory<Program> WebApp { get; }
    public IServiceScope Scope { get; }
    public HttpClient Http { get; }
    public AppOptions AppOptions => WebApp.Services.GetService<IOptions<AppOptions>>()!.Value;

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
    public Apis.ServerInfo ServerInfo1 { get; private set; } = default!;
    public Apis.ServerInfo ServerInfo2 { get; private set; } = default!;
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

    public async Task<IPAddress> NewIpV6()
    {
        return (await NewIpV4()).MapToIPv6();
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
        Http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(JwtBearerDefaults.AuthenticationScheme,
                CreateUserAuthenticationCode(UserSystemAdmin1.Email!));
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
        ServerInfo1 = await NewServerInfo2();
        ServerInfo2 = await NewServerInfo2();

        await using var scope = WebApp.Services.CreateAsyncScope();
        await using var vhContext = scope.ServiceProvider.GetRequiredService<VhContext>();

        await AddUser(vhContext, UserSystemAdmin1);
        await AddUser(vhContext, UserProjectOwner1);
        await AddUser(vhContext, User1);
        await AddUser(vhContext, User2);
        await vhContext.AuthManager.Role_AddUser(AuthManager.SystemAdminRoleId, UserSystemAdmin1.UserId, AuthManager.SystemUserId);
        await vhContext.SaveChangesAsync();

        var projectController = CreateProjectController();
        var certificateController = CreateCertificateController();
        var accessPointGroupController = CreateAccessPointGroupController();

        // create default project
        Project? project;
        if (useSharedProject)
        {
            var sharedProjectId = Guid.Parse("648B9968-7221-4463-B70A-00A10919AE69");
            project = await vhContext.Projects
                .Include(x => x.AccessPointGroups)
                .SingleOrDefaultAsync(x => x.ProjectId == sharedProjectId);

            if (project == null)
            {
                project = await projectController.Create(sharedProjectId);
            }
            else
            {
                // add new owner to shared project
                var ownerRole = (await vhContext.AuthManager.SecureObject_GetRolePermissionGroups(project.ProjectId))
                    .Single(x => x.PermissionGroupId == PermissionGroups.ProjectOwner.PermissionGroupId);
                await vhContext.AuthManager.Role_AddUser(ownerRole.RoleId, UserProjectOwner1.UserId, AuthManager.SystemUserId);
                await vhContext.SaveChangesAsync();
            }
        }
        else
        {
            project = await projectController.Create();
        }

        // create Project1
        ProjectId = project.ProjectId;

        var certificate1 = await certificateController.Create(ProjectId, new CertificateCreateParams { SubjectName = $"CN={PublicServerDns}" });
        AccessPointGroupId1 = (await accessPointGroupController.Create(ProjectId, new AccessPointGroupCreateParams { CertificateId = certificate1.CertificateId })).AccessPointGroupId;

        var certificate2 = await certificateController.Create(ProjectId, new CertificateCreateParams { SubjectName = $"CN={PrivateServerDns}" });
        AccessPointGroupId2 = (await accessPointGroupController.Create(ProjectId, new AccessPointGroupCreateParams { CertificateId = certificate2.CertificateId })).AccessPointGroupId;

        var serverController = CreateServerController();
        var server1 = await serverController.Create(project.ProjectId, new ServerCreateParams());
        var server2 = await serverController.Create(project.ProjectId, new ServerCreateParams());
        ServerId1 = server1.ServerId;
        ServerId2 = server2.ServerId;
        await InitAccessPoint(server1, HostEndPointG1S1, AccessPointGroupId1, AccessPointMode.PublicInToken);
        await InitAccessPoint(server1, HostEndPointG2S1, AccessPointGroupId2, AccessPointMode.Public);
        await InitAccessPoint(server2, HostEndPointG1S2, AccessPointGroupId1, AccessPointMode.Public);
        await InitAccessPoint(server2, HostEndPointG2S2, AccessPointGroupId2, AccessPointMode.PublicInToken);

        // configure servers
        var agentController1 = CreateAgentController2(server1.ServerId);
        var agentController2 = CreateAgentController2(server2.ServerId);
        await agentController1.ConfigureAsync(ServerInfo1);
        await agentController2.ConfigureAsync(ServerInfo2);
        await agentController1.StatusAsync(ServerInfo1.Status);
        await agentController2.StatusAsync(ServerInfo2.Status);

        // Create AccessToken1
        var accessTokenControl = CreateAccessTokenController();
        AccessToken1 = await accessTokenControl.Create(ProjectId,
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
        await Task.Delay(100);

        var agentController = CreateAgentController();
        var accessTokenControl = CreateAccessTokenController();

        // ----------------
        // Create accessToken1 public
        // ----------------
        var accessToken = await accessTokenControl.Create(ProjectId,
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
        var sessionResponseEx = await agentController.Session_Create(sessionRequestEx);
        await agentController.Session_AddUsage(sessionResponseEx.SessionId, closeSession: false, usageInfo: fillData.ItemUsageInfo);
        await agentController.Session_AddUsage(sessionResponseEx.SessionId, closeSession: false, usageInfo: fillData.ItemUsageInfo);
        fillData.SessionResponses.Add(sessionResponseEx);
        fillData.SessionRequests.Add(sessionRequestEx);

        // accessToken1 - sessions2
        sessionRequestEx = CreateSessionRequestEx(accessToken, hostEndPoint: HostEndPointG2S1);
        sessionResponseEx = await agentController.Session_Create(sessionRequestEx);
        await agentController.Session_AddUsage(sessionResponseEx.SessionId, closeSession: false, usageInfo: fillData.ItemUsageInfo);
        await agentController.Session_AddUsage(sessionResponseEx.SessionId, closeSession: false, usageInfo: fillData.ItemUsageInfo);
        fillData.SessionResponses.Add(sessionResponseEx);
        fillData.SessionRequests.Add(sessionRequestEx);

        // ----------------
        // Create accessToken2 public
        // ----------------
        accessToken = await accessTokenControl.Create(ProjectId,
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
        sessionResponseEx = await agentController.Session_Create(sessionRequestEx);
        await agentController.Session_AddUsage(sessionResponseEx.SessionId, closeSession: false, usageInfo: fillData.ItemUsageInfo);
        await agentController.Session_AddUsage(sessionResponseEx.SessionId, closeSession: false, usageInfo: fillData.ItemUsageInfo);
        fillData.SessionResponses.Add(sessionResponseEx);
        fillData.SessionRequests.Add(sessionRequestEx);

        // accessToken2 - sessions2
        sessionRequestEx = CreateSessionRequestEx(accessToken);
        sessionResponseEx = await agentController.Session_Create(sessionRequestEx);
        await agentController.Session_AddUsage(sessionResponseEx.SessionId, closeSession: false, usageInfo: fillData.ItemUsageInfo);
        await agentController.Session_AddUsage(sessionResponseEx.SessionId, closeSession: false, usageInfo: fillData.ItemUsageInfo);
        fillData.SessionResponses.Add(sessionResponseEx);
        fillData.SessionRequests.Add(sessionRequestEx);

        // ----------------
        // Create accessToken3 private
        // ----------------
        accessToken = await accessTokenControl.Create(ProjectId,
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
        sessionResponseEx = await agentController.Session_Create(sessionRequestEx);
        await agentController.Session_AddUsage(sessionResponseEx.SessionId, closeSession: false, usageInfo: fillData.ItemUsageInfo);
        await agentController.Session_AddUsage(sessionResponseEx.SessionId, closeSession: false, usageInfo: fillData.ItemUsageInfo);
        fillData.SessionResponses.Add(sessionResponseEx);
        fillData.SessionRequests.Add(sessionRequestEx);

        // accessToken3 - sessions2
        // actualAccessCount++; it is private!
        sessionRequestEx = CreateSessionRequestEx(accessToken);
        sessionResponseEx = await agentController.Session_Create(sessionRequestEx);
        await agentController.Session_AddUsage(sessionResponseEx.SessionId, closeSession: false, usageInfo: fillData.ItemUsageInfo);
        await agentController.Session_AddUsage(sessionResponseEx.SessionId, closeSession: false, usageInfo: fillData.ItemUsageInfo);
        fillData.SessionResponses.Add(sessionResponseEx);
        fillData.SessionRequests.Add(sessionRequestEx);

        return fillData;
    }

    private async Task InitAccessPoint(Models.Server server,
        IPEndPoint hostEndPoint,
        Guid accessPointGroupId,
        AccessPointMode accessPointMode, bool isListen = true)
    {
        // create server accessPoints
        var accessPointController = CreateAccessPointController();
        await accessPointController.Create(ProjectId,
            new AccessPointCreateParams(server.ServerId, hostEndPoint.Address, accessPointGroupId)
            {
                TcpPort = hostEndPoint.Port,
                IsListen = isListen,
                AccessPointMode = accessPointMode,
            }
        );
    }

    public static ServerStatus NewServerStatus()
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
        };
    }

    public static Apis.ServerStatus NewServerStatus2()
    {
        var rand = new Random();
        return new Apis.ServerStatus
        {
            SessionCount = rand.Next(1, 1000),
            FreeMemory = rand.Next(150, 300) * 1000000000L,
            UsedMemory = rand.Next(0, 150) * 1000000000L,
            TcpConnectionCount = rand.Next(100, 500),
            UdpConnectionCount = rand.Next(501, 1000),
            ThreadCount = rand.Next(0, 50),
        };
    }

    public async Task<ServerInfo> NewServerInfo()
    {
        var rand = new Random();
        var publicIp = await NewIpV6();
        var serverInfo = new ServerInfo(
            Version.Parse($"1.{rand.Next(0, 255)}.{rand.Next(0, 255)}.{rand.Next(0, 255)}"),
            Environment.Version,
            new[]
            {
                IPAddress.Parse($"192.168.{rand.Next(0, 255)}.{rand.Next(0, 255)}"),
                IPAddress.Parse($"192.168.{rand.Next(0, 255)}.{rand.Next(0, 255)}"),
                publicIp
            },
            new[]
            {
                await NewIpV4(),
                await NewIpV6(),
                publicIp,
            },
            NewServerStatus()
        )
        {
            MachineName = $"MachineName-{Guid.NewGuid()}",
            OsInfo = $"{Environment.OSVersion.Platform}-{Guid.NewGuid()}"
        };

        return serverInfo;
    }

    public async Task<Apis.ServerInfo> NewServerInfo2()
    {
        var rand = new Random();
        var publicIp = await NewIpV6();
        var serverInfo = new Apis.ServerInfo
        {
            Version = Version.Parse($"1.{rand.Next(0, 255)}.{rand.Next(0, 255)}.{rand.Next(0, 255)}").ToString(),
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
            Status = NewServerStatus2(),
            MachineName = $"MachineName-{Guid.NewGuid()}",
            OsInfo = $"{Environment.OSVersion.Platform}-{Guid.NewGuid()}"
        };

        return serverInfo;
    }

    public SessionRequestEx CreateSessionRequestEx(AccessToken? accessToken = null, Guid? clientId = null, IPEndPoint? hostEndPoint = null, IPAddress? clientIp = null)
    {
        accessToken ??= AccessToken1;
        var rand = new Random();

        var clientInfo = new ClientInfo
        {
            ClientId = clientId ?? Guid.NewGuid(),
            ClientVersion = $"999.{rand.Next(0, 999)}.{rand.Next(0, 999)}"
        };

        var sessionRequestEx = new SessionRequestEx(
            accessToken.AccessTokenId,
            clientInfo,
            Util.EncryptClientId(clientInfo.ClientId, accessToken.Secret),
            hostEndPoint ?? HostEndPointG1S1)
        {
            ClientIp = clientIp
        };

        return sessionRequestEx;
    }

    public Apis.SessionRequestEx CreateSessionRequestEx2(Apis.AccessToken accessToken, Guid? clientId = null, IPEndPoint? hostEndPoint = null, IPAddress? clientIp = null)
    {
        var rand = new Random();

        var clientInfo = new Apis.ClientInfo
        {
            ClientId = clientId ?? Guid.NewGuid(),
            ClientVersion = $"999.{rand.Next(0, 999)}.{rand.Next(0, 999)}",
            UserAgent = "agent"
        };

        var sessionRequestEx = new Apis.SessionRequestEx
        {
            TokenId = accessToken.AccessTokenId,
            ClientInfo = clientInfo,
            EncryptedClientId = Util.EncryptClientId(clientInfo.ClientId, accessToken.Secret),
            HostEndPoint = hostEndPoint?.ToString() ?? HostEndPointG1S1.ToString(),
            ClientIp = clientIp?.ToString() ?? NewIpV4().Result.ToString()
        };

        return sessionRequestEx;
    }


    public static ILogger<T> CreateConsoleLogger<T>(bool verbose = false)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(config => { config.IncludeScopes = true; });
            builder.SetMinimumLevel(verbose ? LogLevel.Trace : LogLevel.Information);
        });
        var logger = loggerFactory.CreateLogger<T>();
        return logger;
    }

    private ControllerContext CreateControllerContext(string? userEmail)
    {
        userEmail ??= UserProjectOwner1.Email ?? throw new InvalidOperationException($"{nameof(UserProjectOwner1)} is not set!");

        var httpContext = new DefaultHttpContext();
        var claimsIdentity = new ClaimsIdentity(
            new[] {
                new Claim(ClaimTypes.NameIdentifier, userEmail),
                new Claim("emails", userEmail),
                new Claim("iss", "auth")
            });
        httpContext.User = new ClaimsPrincipal(claimsIdentity);
        httpContext.Request.Host = new HostString("test.vpnhood.com");
        httpContext.Request.Scheme = "https://";

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ControllerActionDescriptor());

        return new ControllerContext(actionContext);
    }

    public AccessTokenController CreateAccessTokenController(string? userEmail = null)
    {
        var controller = new AccessTokenController(CreateConsoleLogger<AccessTokenController>(true),
            Scope.ServiceProvider.GetRequiredService<VhContext>(),
            Scope.ServiceProvider.GetRequiredService<VhReportContext>(),
            Scope.ServiceProvider.GetRequiredService<IMemoryCache>())
        {
            ControllerContext = CreateControllerContext(userEmail)
        };
        return controller;
    }

    public AccessController CreateAccessController(string? userEmail = null)
    {
        var controller = new AccessController(CreateConsoleLogger<AccessController>(true),
            Scope.ServiceProvider.GetRequiredService<VhContext>())
        {
            ControllerContext = CreateControllerContext(userEmail)
        };
        return controller;
    }

    public AccessPointController CreateAccessPointController(string? userEmail = null)
    {
        var controller = new AccessPointController(
            CreateConsoleLogger<AccessPointController>(true),
            Scope.ServiceProvider.GetRequiredService<VhContext>())
        {
            ControllerContext = CreateControllerContext(userEmail)
        };
        return controller;
    }

    public ProjectController CreateProjectController(string? userEmail = null)
    {
        var controller = new ProjectController(
            Scope.ServiceProvider.GetRequiredService<ILogger<ProjectController>>(),
            Scope.ServiceProvider.GetRequiredService<VhContext>(),
            Scope.ServiceProvider.GetRequiredService<VhReportContext>(),
            Scope.ServiceProvider.GetRequiredService<IMemoryCache>(),
            Scope.ServiceProvider.GetRequiredService<IOptions<AppOptions>>())
        {
            ControllerContext = CreateControllerContext(userEmail)
        };
        return controller;
    }

    public AccessPointGroupController CreateAccessPointGroupController(string? userEmail = null)
    {
        var controller = new AccessPointGroupController(
            Scope.ServiceProvider.GetRequiredService<ILogger<AccessPointGroupController>>(),
            Scope.ServiceProvider.GetRequiredService<VhContext>()
            )
        {
            ControllerContext = CreateControllerContext(userEmail)
        };
        return controller;
    }

    public RoleController CreateRoleController(string? userEmail = null)
    {
        var controller = new RoleController(
            Scope.ServiceProvider.GetRequiredService<ILogger<RoleController>>(),
            Scope.ServiceProvider.GetRequiredService<VhContext>())
        {
            ControllerContext = CreateControllerContext(userEmail)
        };
        return controller;
    }

    public UserController CreateUserController(string? userEmail = null)
    {
        var controller = new UserController(
            Scope.ServiceProvider.GetRequiredService<ILogger<UserController>>(),
            Scope.ServiceProvider.GetRequiredService<VhContext>())
        {
            ControllerContext = CreateControllerContext(userEmail)
        };
        return controller;
    }

    public AgentController CreateAgentController(Guid? serverId = null)
    {
        serverId ??= ServerId1;

        using var scope = WebApp.Services.CreateAsyncScope();
        using var vhContext = scope.ServiceProvider.GetRequiredService<VhContext>();
        var server = vhContext.Servers.Single(x => x.ServerId == serverId);

        var httpContext = new DefaultHttpContext();
        var claimsIdentity = new ClaimsIdentity(
            new[] {
                new Claim(ClaimTypes.NameIdentifier, serverId.ToString()!),
                new Claim("authorization_code", server.AuthorizationCode.ToString()),
                new Claim("iss", "auth"),
            });
        httpContext.User = new ClaimsPrincipal(claimsIdentity);

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ControllerActionDescriptor());

        var controller = new AgentController(
            Scope.ServiceProvider.GetRequiredService<ILogger<AgentController>>(),
            Scope.ServiceProvider.GetRequiredService<VhContext>(),
            WebApp.Services.GetRequiredService<ServerManager>(),
            WebApp.Services.GetRequiredService<IOptions<AppOptions>>())
        {
            ControllerContext = new ControllerContext(actionContext)
        };
        return controller;
    }

    public ServerController CreateServerController(string? userEmail = null)
    {
        var controller = new ServerController(
            Scope.ServiceProvider.GetRequiredService<ILogger<ServerController>>(),
            Scope.ServiceProvider.GetRequiredService<VhContext>(),
            Scope.ServiceProvider.GetRequiredService<VhReportContext>(),
            Scope.ServiceProvider.GetRequiredService<IOptions<AppOptions>>())
        {
            ControllerContext = CreateControllerContext(userEmail)
        };
        return controller;
    }

    public IpLockController CreateIpLockController(string? userEmail = null)
    {
        var controller = new IpLockController(
            CreateConsoleLogger<IpLockController>(true),
            Scope.ServiceProvider.GetRequiredService<VhContext>())
        {
            ControllerContext = CreateControllerContext(userEmail)
        };
        return controller;
    }


    public DeviceController CreateDeviceController(string? userEmail = null)
    {
        var controller = new DeviceController(
            CreateConsoleLogger<DeviceController>(true),
            Scope.ServiceProvider.GetRequiredService<VhContext>())
        {
            ControllerContext = CreateControllerContext(userEmail)
        };
        return controller;
    }

    public CertificateController CreateCertificateController(string? userEmail = null)
    {
        var controller = new CertificateController(
            CreateConsoleLogger<CertificateController>(true),
            Scope.ServiceProvider.GetRequiredService<VhContext>())
        {
            ControllerContext = CreateControllerContext(userEmail)
        };
        return controller;
    }

    public Task SyncToReport()
    {
        var logger = WebApp.Services.GetRequiredService<ILogger<SyncManager>>();
        var syncManager = new SyncManager(logger, WebApp.Services);
        return syncManager.Sync();
    }

    public Apis.AgentController CreateAgentController2(Guid? serverId = null)
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
        return new Apis.AgentController(http);
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
}