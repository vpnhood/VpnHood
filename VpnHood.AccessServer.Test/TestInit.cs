using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Authorization;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Common.Logging;
using VpnHood.Server;
using VpnHood.Server.Messaging;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class TestInit
    {
        public User UserSystemAdmin1 { get; } = NewUser("Administrator1");
        public User UserProjectOwner1 { get; } = NewUser("Project Owner 1");
        public User User1 { get; } = NewUser("User1");
        public User User2 { get; } = NewUser("User2");
        public Guid ProjectId { get; private set; }
        public Guid ServerId1 { get; private set; }
        public Guid ServerId2 { get; private set; } 
        public string PublicServerDns { get; } = $"publicfoo.{Guid.NewGuid()}.com";
        public string PrivateServerDns { get; } = $"privatefoo.{Guid.NewGuid()}.com";
        public IPEndPoint HostEndPointG1S1 { get; private set; } = null!;
        public IPEndPoint HostEndPointG1S2 { get; private set; } = null!;
        public IPEndPoint HostEndPointG2S1 { get; private set; } = null!;
        public IPEndPoint HostEndPointG2S2 { get; private set; } = null!;
        public IPEndPoint HostEndPointNew1 { get; private set; } = null!;
        public IPEndPoint HostEndPointNew2 { get; private set; } = null!;
        public IPEndPoint HostEndPointNew3 { get; private set; } = null!;
        public IPAddress ClientIp1 { get; private set; } = null!;
        public IPAddress ClientIp2 { get; private set; } = null!;
        public AccessToken AccessToken1 { get; private set; } = null!;
        public Guid AccessPointGroupId1 { get; private set; }
        public Guid AccessPointGroupId2 { get; private set; }

        public static async Task<IPAddress> NewIp()
        {
            await using var vhContext = new VhContext();
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

        public static async Task<IPEndPoint> NewEndPoint()
            => new(await NewIp(), 443);

        public static User NewUser(string name)
        {
            var userId = Guid.NewGuid();
            return new User
            {
                UserId = userId,
                AuthUserId = userId.ToString(),
                Email = $"{userId}@vpnhood.com",
                UserName = $"{name}_{userId}",
                MaxProjectCount = AccessServerApp.Instance.UserMaxProjectCount,
                CreatedTime = DateTime.UtcNow
            };
        }


        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext _)
        {
            AccessServerApp app = new();
            app.Start(new[] { "/testmode" });
            VhLogger.IsDiagnoseMode = true;
        }

        private static async Task AddUser(VhContext vhContext, User user)
        {
            await vhContext.Users.AddAsync(user);
            var secureObject = await vhContext.AuthManager.CreateSecureObject(user.UserId, SecureObjectTypes.User);
            await vhContext.AuthManager.SecureObject_AddUserPermission(secureObject, user.UserId, PermissionGroups.ProjectViewer, user.UserId);
        }

        public async Task Init(bool useSharedProject = false)
        {
            HostEndPointG1S1 = await NewEndPoint();
            HostEndPointG1S2 = await NewEndPoint();
            HostEndPointG2S1 = await NewEndPoint();
            HostEndPointG2S2 = await NewEndPoint();
            HostEndPointNew1 = await NewEndPoint();
            HostEndPointNew2 = await NewEndPoint();
            HostEndPointNew3 = await NewEndPoint();
            ClientIp1 = await NewIp();
            ClientIp2 = await NewIp();

            await using var vhContext = new VhContext();
            
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
            Project project;
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
            AccessPointGroupId1 = (await accessPointGroupController.Create(ProjectId, new AccessPointGroupCreateParams { CertificateId = certificate1.CertificateId, MakeDefault = true })).AccessPointGroupId;

            var certificate2 = await certificateController.Create(ProjectId, new CertificateCreateParams { SubjectName = $"CN={PrivateServerDns}" });
            AccessPointGroupId2 = (await accessPointGroupController.Create(ProjectId, new AccessPointGroupCreateParams { CertificateId = certificate2.CertificateId })).AccessPointGroupId;

            // create servers
            var serverController = CreateServerController();
            ServerId1 = (await serverController.Create(project.ProjectId, new ServerCreateParams())).ServerId;
            ServerId2 = (await serverController.Create(project.ProjectId, new ServerCreateParams())).ServerId;

            // subscribe servers
            var accessController1 = CreateAccessController(ServerId1);
            var accessController2 = CreateAccessController(ServerId2);
            await accessController1.ServerSubscribe(new ServerInfo(Version.Parse("1.0.0")) { EnvironmentVersion = Environment.Version });
            await accessController2.ServerSubscribe(new ServerInfo(Version.Parse("1.0.0")) { EnvironmentVersion = Environment.Version });

            // Create AccessToken1
            var accessTokenControl = CreateAccessTokenController();
            AccessToken1 = await accessTokenControl.Create(ProjectId,
                new AccessTokenCreateParams
                {
                    Secret = Util.GenerateSessionKey(),
                    AccessTokenName = $"Access1_{Guid.NewGuid()}",
                    AccessPointGroupId = AccessPointGroupId1
                });

            // create accessPoints
            var accessPointController = CreateAccessPointController();
            await accessPointController.Create(ProjectId, ServerId1,
                new AccessPointCreateParams { PublicIpAddress = HostEndPointG1S1.Address, TcpPort = HostEndPointG1S1.Port, AccessPointGroupId = AccessPointGroupId1, IncludeInAccessToken = true });

            await accessPointController.Create(ProjectId, ServerId1,
                new AccessPointCreateParams { PublicIpAddress = HostEndPointG1S2.Address, TcpPort= HostEndPointG1S2.Port, AccessPointGroupId = AccessPointGroupId1 });

            await accessPointController.Create(ProjectId, ServerId1,
                new AccessPointCreateParams { PublicIpAddress = HostEndPointG2S1.Address, TcpPort = HostEndPointG2S1.Port, AccessPointGroupId = AccessPointGroupId2, IncludeInAccessToken = true });

            await accessPointController.Create(ProjectId, ServerId1,
                new AccessPointCreateParams { PublicIpAddress = HostEndPointG2S2.Address, TcpPort = HostEndPointG2S2.Port, AccessPointGroupId = AccessPointGroupId2 });
        }

        public SessionRequestEx CreateSessionRequestEx(AccessToken? accessToken = null, Guid? clientId = null, IPEndPoint? hostEndPoint = null, IPAddress? clientIp = null)
        {
            accessToken ??= AccessToken1;

            var clientInfo = new ClientInfo
            {
                ClientId = clientId ?? Guid.NewGuid(),
                ClientVersion = "1.1.1"
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

        private ControllerContext CreateControllerContext(string? userEmail, Guid? projectId = null)
        {
            userEmail ??= UserProjectOwner1.Email ?? throw new InvalidOperationException($"{nameof(UserProjectOwner1)} is not set!");

            DefaultHttpContext httpContext = new();
            ClaimsIdentity claimsIdentity = new(
                new[] {
                    new Claim(ClaimTypes.NameIdentifier, userEmail),
                    new Claim("emails", userEmail),
                    new Claim("iss", "auth"),
                    projectId!=null ? new Claim("project_id", projectId.ToString()!) : new Claim("fake_header", "ff")
                });
            httpContext.User = new ClaimsPrincipal(claimsIdentity);

            ActionContext actionContext = new(
                httpContext,
                new RouteData(),
                new ControllerActionDescriptor());

            return new ControllerContext(actionContext);
        }

        public AccessTokenController CreateAccessTokenController(string? userEmail = null)
        {
            var controller = new AccessTokenController(CreateConsoleLogger<AccessTokenController>(true))
            {
                ControllerContext = CreateControllerContext(userEmail)
            };
            return controller;
        }

        public AccessPointController CreateAccessPointController(string? userEmail = null)
        {
            var controller = new AccessPointController(CreateConsoleLogger<AccessPointController>(true))
            {
                ControllerContext = CreateControllerContext(userEmail)
            };
            return controller;
        }

        public ProjectController CreateProjectController(string? userEmail = null)
        {
            var controller = new ProjectController(CreateConsoleLogger<ProjectController>(true))
            {
                ControllerContext = CreateControllerContext(userEmail)
            };
            return controller;
        }

        public AccessPointGroupController CreateAccessPointGroupController(string? userEmail = null)
        {
            var controller = new AccessPointGroupController(CreateConsoleLogger<AccessPointGroupController>(true))
            {
                ControllerContext = CreateControllerContext(userEmail)
            };
            return controller;
        }

        public RoleController CreateRoleController(string? userEmail = null)
        {
            var controller = new RoleController(CreateConsoleLogger<RoleController>(true))
            {
                ControllerContext = CreateControllerContext(userEmail)
            };
            return controller;
        }

        public UserController CreateUserController(string? userEmail = null)
        {
            var controller = new UserController(CreateConsoleLogger<UserController>(true))
            {
                ControllerContext = CreateControllerContext(userEmail)
            };
            return controller;
        }

        public AccessController CreateAccessController(Guid? serverId = null)
        {
            serverId ??= ServerId1;

            var vhContext = new VhContext();
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

            var controller = new AccessController(CreateConsoleLogger<AccessController>(true))
            {
                ControllerContext = new ControllerContext(actionContext)
            };
            return controller;
        }

        public ServerController CreateServerController(string? userEmail = null)
        {
            var controller = new ServerController(CreateConsoleLogger<ServerController>(true))
            {
                ControllerContext = CreateControllerContext(userEmail)
            };
            return controller;
        }

        public ClientController CreateClientController(string? userEmail = null)
        {
            var controller = new ClientController(CreateConsoleLogger<ClientController>(true))
            {
                ControllerContext = CreateControllerContext(userEmail)
            };
            return controller;
        }

        public CertificateController CreateCertificateController(string? userEmail = null)
        {
            var controller = new CertificateController(CreateConsoleLogger<CertificateController>(true))
            {
                ControllerContext = CreateControllerContext(userEmail)
            };
            return controller;
        }
    }
}
