using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.Models;
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Logging;
using VpnHood.Server.Messaging;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class TestInit
    {
        public const string USER_Admin = "admin";
        public const string USER_VpnServer = "user_vpn_server";
        public Guid ProjectId { get; private set; }
        public Guid ServerId_1 { get; private set; } = Guid.NewGuid();
        public Guid ServerId_2 { get; private set; } = Guid.NewGuid();
        public string PublicServerDns { get; private set; } = $"publicfoo.{Guid.NewGuid()}.com";
        public string PrivateServerDns { get; private set; } = $"privatefoo.{Guid.NewGuid()}.com";
        public IPEndPoint HostEndPoint_G1S1 { get; private set; } = null!;
        public IPEndPoint HostEndPoint_G1S2 { get; private set; } = null!;
        public IPEndPoint HostEndPoint_G2S1 { get; private set; } = null!;
        public IPEndPoint HostEndPoint_G2S2 { get; private set; } = null!;
        public IPEndPoint HostEndPoint_New1 { get; private set; } = null!;
        public IPEndPoint HostEndPoint_New2 { get; private set; } = null!;
        public IPEndPoint HostEndPoint_New3 { get; private set; } = null!;
        public IPAddress ClientIp1 { get; private set; } = null!;
        public IPAddress ClientIp2 { get; private set; } = null!;
        public Guid AccessTokenId_1 { get; private set; }
        public byte[] AccessTokenSecret_1 { get; private set; } = Util.GenerateSessionKey();
        public Guid AccessTokenGroupId_1 { get; private set; }
        public Guid AccessTokenGroupId_2 { get; private set; }

        public static async Task<IPAddress> NewIp()
        {
            VhContext vhContext = new();
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
            => new IPEndPoint(await NewIp(), port: 443);

        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext _)
        {
            AccessServerApp app = new();
            app.Start(args: new[] { "/testmode" });
            VhLogger.IsDiagnoseMode = true;
        }

        public async Task Init(bool useSharedProject = false)
        {
            HostEndPoint_G1S1 = await NewEndPoint();
            HostEndPoint_G1S2 = await NewEndPoint();
            HostEndPoint_G2S1 = await NewEndPoint();
            HostEndPoint_G2S2 = await NewEndPoint();
            HostEndPoint_New1 = await NewEndPoint();
            HostEndPoint_New2 = await NewEndPoint();
            HostEndPoint_New3 = await NewEndPoint();
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
            AccessTokenGroupId_1 = project1.AccessTokenGroups!.Single(x => x.IsDefault).AccessTokenGroupId;

            var accessTokenGroupController = CreateAccessTokenGroupController();
            AccessTokenGroupId_2 = (await accessTokenGroupController.Create(ProjectId, $"Group2_{Guid.NewGuid()}")).AccessTokenGroupId;

            // Create AccessToken1
            var accessTokenControl = CreateAccessTokenController();
            AccessTokenId_1 = (await accessTokenControl.Create(ProjectId,
                createParams: new()
                {
                    Secret = AccessTokenSecret_1,
                    AccessTokenName = $"Access1_{Guid.NewGuid()}",
                    AccessTokenGroupId = AccessTokenGroupId_1
                })).AccessTokenId;

            // create serverEndPoints
            var serverEndPointController = CreateServerEndPointController();
            await serverEndPointController.Create(ProjectId, HostEndPoint_G1S1.ToString(),
                createParams: new() { AccessTokenGroupId = AccessTokenGroupId_1, SubjectName = $"CN={PublicServerDns}", MakeDefault = true });

            await serverEndPointController.Create(ProjectId, HostEndPoint_G1S2.ToString(),
                createParams: new() { AccessTokenGroupId = AccessTokenGroupId_1, SubjectName = $"CN={PublicServerDns}" });

            await serverEndPointController.Create(ProjectId, HostEndPoint_G2S1.ToString(),
                createParams: new() { AccessTokenGroupId = AccessTokenGroupId_2, SubjectName = $"CN={PrivateServerDns}", MakeDefault = true });

            await serverEndPointController.Create(ProjectId, HostEndPoint_G2S2.ToString(),
                createParams: new() { AccessTokenGroupId = AccessTokenGroupId_2, SubjectName = $"CN={PrivateServerDns}" });

            // subscribe servers
            var accessController = CreateAccessController();
            await accessController.ServerSubscribe(ServerId_1, new(Version.Parse("1.0.0")) { EnvironmentVersion = Environment.Version });
            await accessController.ServerSubscribe(ServerId_2, new(Version.Parse("1.0.0")) { EnvironmentVersion = Environment.Version });
        }

        public SessionRequestEx CreateSessionRequestEx(AccessToken? accessToken = null, Guid? clientId = null, IPEndPoint? hostEndPoint = null, IPAddress? clientIp = null)
        {
            var clientInfo = new ClientInfo()
            {
                ClientId = clientId ?? Guid.NewGuid(),
                ClientVersion = "1.1.1"
            };

            var sessionRequestEx = new SessionRequestEx(
                tokenId: accessToken?.AccessTokenId ?? AccessTokenId_1,
                clientInfo: clientInfo,
                encryptedClientId: Util.EncryptClientId(clientInfo.ClientId, accessToken?.Secret ?? AccessTokenSecret_1),
                hostEndPoint ?? HostEndPoint_G1S1)
            {
                ClientIp = clientIp
            };

            return sessionRequestEx;
        }

        public static ILogger<T> CreateConsoleLogger<T>(bool verbose = false)
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddSimpleConsole((config) => { config.IncludeScopes = true; });
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
            httpContext.User = new(claimsIdentity);

            ActionContext actionContext = new(
                httpContext,
                new Microsoft.AspNetCore.Routing.RouteData(),
                new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor());

            return new ControllerContext(actionContext);
        }

        public static AccessTokenController CreateAccessTokenController(string userId = USER_Admin)
        {
            var controller = new AccessTokenController(CreateConsoleLogger<AccessTokenController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public static ServerEndPointController CreateServerEndPointController(string userId = USER_Admin)
        {
            var controller = new ServerEndPointController(CreateConsoleLogger<ServerEndPointController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public static ProjectController CreateProjectController(string userId = USER_Admin)
        {
            var controller = new ProjectController(CreateConsoleLogger<ProjectController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public static AccessTokenGroupController CreateAccessTokenGroupController(string userId = USER_Admin)
        {
            var controller = new AccessTokenGroupController(CreateConsoleLogger<AccessTokenGroupController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public AccessController CreateAccessController(string userId = USER_VpnServer)
        {
            var controller = new AccessController(CreateConsoleLogger<AccessController>(true))
            {
                ControllerContext = CreateControllerContext(userId, ProjectId)
            };
            return controller;
        }

        public static ServerController CreateServerController(string userId = USER_VpnServer)
        {
            var controller = new ServerController(CreateConsoleLogger<ServerController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public static ClientController CreateClientController(string userId = USER_VpnServer)
        {
            var controller = new ClientController(CreateConsoleLogger<ClientController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }
    }
}
