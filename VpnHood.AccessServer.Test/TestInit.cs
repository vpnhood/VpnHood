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
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
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
        public const string UserAdmin = "admin";
        public const string UserVpnServer = "user_vpn_server";
        public Guid ProjectId { get; private set; }
        public Guid ServerId1 { get; } = Guid.NewGuid();
        public Guid ServerId2 { get; } = Guid.NewGuid();
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
        public Guid AccessTokenGroupId1 { get; private set; }
        public Guid AccessTokenGroupId2 { get; private set; }

        public static async Task<IPAddress> NewIp()
        {
            await using VhContext vhContext = new();
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

        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext _)
        {
            AccessServerApp app = new();
            app.Start(new[] { "/testmode" });
            VhLogger.IsDiagnoseMode = true;
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

            await using VhContext vhContext = new();
            var projectController = CreateProjectController();

            // create default project
            var sharedProjectId = Guid.Parse("648B9968-7221-4463-B70A-00A10919AE69");
            var sharedProject = await vhContext.Projects
                .Include(x => x.AccessTokenGroups)
                .SingleOrDefaultAsync(x => x.ProjectId == sharedProjectId) 
                                ?? await projectController.Create(sharedProjectId);

            // create Project1
            var project1 = useSharedProject ? sharedProject : await projectController.Create();
            ProjectId = project1.ProjectId;
            AccessTokenGroupId1 = project1.AccessTokenGroups!.Single(x => x.IsDefault).AccessTokenGroupId;

            var accessTokenGroupController = CreateAccessTokenGroupController();
            AccessTokenGroupId2 = (await accessTokenGroupController.Create(ProjectId, $"Group2_{Guid.NewGuid()}")).AccessTokenGroupId;

            // Create AccessToken1
            var accessTokenControl = CreateAccessTokenController();
            AccessToken1 = await accessTokenControl.Create(ProjectId,
                new AccessTokenCreateParams
                {
                    Secret = Util.GenerateSessionKey(),
                    AccessTokenName = $"Access1_{Guid.NewGuid()}",
                    AccessTokenGroupId = AccessTokenGroupId1
                });

            // create serverEndPoints
            var serverEndPointController = CreateServerEndPointController();
            await serverEndPointController.Create(ProjectId, HostEndPointG1S1.ToString(),
                new ServerEndPointCreateParams { AccessTokenGroupId = AccessTokenGroupId1, SubjectName = $"CN={PublicServerDns}", MakeDefault = true });

            await serverEndPointController.Create(ProjectId, HostEndPointG1S2.ToString(),
                new ServerEndPointCreateParams { AccessTokenGroupId = AccessTokenGroupId1, SubjectName = $"CN={PublicServerDns}" });

            await serverEndPointController.Create(ProjectId, HostEndPointG2S1.ToString(),
                new ServerEndPointCreateParams { AccessTokenGroupId = AccessTokenGroupId2, SubjectName = $"CN={PrivateServerDns}", MakeDefault = true });

            await serverEndPointController.Create(ProjectId, HostEndPointG2S2.ToString(),
                new ServerEndPointCreateParams { AccessTokenGroupId = AccessTokenGroupId2, SubjectName = $"CN={PrivateServerDns}" });

            // subscribe servers
            var accessController = CreateAccessController();
            await accessController.ServerSubscribe(ServerId1, new ServerInfo(Version.Parse("1.0.0")) { EnvironmentVersion = Environment.Version });
            await accessController.ServerSubscribe(ServerId2, new ServerInfo(Version.Parse("1.0.0")) { EnvironmentVersion = Environment.Version });
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

        private static ControllerContext CreateControllerContext(string userId, Guid? projectId = null)
        {
            DefaultHttpContext httpContext = new();
            ClaimsIdentity claimsIdentity = new(
                new[] {
                    new Claim(ClaimTypes.NameIdentifier, userId),
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

        public static AccessTokenController CreateAccessTokenController(string userId = UserAdmin)
        {
            var controller = new AccessTokenController(CreateConsoleLogger<AccessTokenController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public static ServerEndPointController CreateServerEndPointController(string userId = UserAdmin)
        {
            var controller = new ServerEndPointController(CreateConsoleLogger<ServerEndPointController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public static ProjectController CreateProjectController(string userId = UserAdmin)
        {
            var controller = new ProjectController(CreateConsoleLogger<ProjectController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public static AccessTokenGroupController CreateAccessTokenGroupController(string userId = UserAdmin)
        {
            var controller = new AccessTokenGroupController(CreateConsoleLogger<AccessTokenGroupController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public AccessController CreateAccessController(string userId = UserVpnServer)
        {
            var controller = new AccessController(CreateConsoleLogger<AccessController>(true))
            {
                ControllerContext = CreateControllerContext(userId, ProjectId)
            };
            return controller;
        }

        public static ServerController CreateServerController(string userId = UserVpnServer)
        {
            var controller = new ServerController(CreateConsoleLogger<ServerController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public static ClientController CreateClientController(string userId = UserVpnServer)
        {
            var controller = new ClientController(CreateConsoleLogger<ClientController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }
    }
}
