﻿using System;
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
        public const string UserVpnServer = "user_vpn_server";
        public User AdminUser { get; } = NewUser("Admin");
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
        public Guid AccessPointGroupId1 { get; private set; }
        public Guid AccessPointGroupId2 { get; private set; }

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

        public static User NewUser(string name)
        {
            var userId = Guid.NewGuid();
            return new User
            {
                UserId = userId,
                AuthUserId = userId.ToString(),
                Email = $"{userId}@vpnhood.com",
                UserName = $"{name}_{userId}"
            };
        }


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
            await vhContext.Users.AddAsync(AdminUser);
            await vhContext.SaveChangesAsync();

            var projectController = CreateProjectController();
            var certificateController = CreateCertificateController();
            var accessPointGroupController = CreateAccessPointGroupController();

            // create default project
            var sharedProjectId = Guid.Parse("648B9968-7221-4463-B70A-00A10919AE69");
            var sharedProject = await vhContext.Projects
                .Include(x => x.AccessPointGroups)
                .SingleOrDefaultAsync(x => x.ProjectId == sharedProjectId)
                                ?? await projectController.Create(sharedProjectId);

            // create Project1
            var project1 = useSharedProject ? sharedProject : await projectController.Create();
            ProjectId = project1.ProjectId;

            var certificate1 = await certificateController.Create(ProjectId, new CertificateCreateParams { SubjectName = $"CN={PublicServerDns}" });
            AccessPointGroupId1 = (await accessPointGroupController.Create(ProjectId, new AccessPointGroupCreateParams { CertificateId = certificate1.CertificateId, MakeDefault = true })).AccessPointGroupId;

            var certificate2 = await certificateController.Create(ProjectId, new CertificateCreateParams { SubjectName = $"CN={PrivateServerDns}" });
            AccessPointGroupId2 = (await accessPointGroupController.Create(ProjectId, new AccessPointGroupCreateParams { CertificateId = certificate2.CertificateId })).AccessPointGroupId;

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
            await accessPointController.Create(ProjectId,
                new AccessPointCreateParams { PublicEndPoint = HostEndPointG1S1, AccessPointGroupId = AccessPointGroupId1, MakeDefault = true });

            await accessPointController.Create(ProjectId,
                new AccessPointCreateParams { PublicEndPoint = HostEndPointG1S2, AccessPointGroupId = AccessPointGroupId1 });

            await accessPointController.Create(ProjectId,
                new AccessPointCreateParams { PublicEndPoint = HostEndPointG2S1, AccessPointGroupId = AccessPointGroupId2, MakeDefault = true });

            await accessPointController.Create(ProjectId,
                new AccessPointCreateParams { PublicEndPoint = HostEndPointG2S2, AccessPointGroupId = AccessPointGroupId2 });

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

        private ControllerContext CreateControllerContext(string? userEmail, Guid? projectId = null)
        {
            userEmail ??= AdminUser.Email ?? throw new InvalidOperationException($"{nameof(AdminUser)} is not set!");

            DefaultHttpContext httpContext = new();
            ClaimsIdentity claimsIdentity = new(
                new[] {
                    new Claim(ClaimTypes.NameIdentifier, userEmail),
                    new Claim(ClaimTypes.Email, userEmail),
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

        public AccessController CreateAccessController(string userEmail = UserVpnServer)
        {
            var controller = new AccessController(CreateConsoleLogger<AccessController>(true))
            {
                ControllerContext = CreateControllerContext(userEmail, ProjectId)
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
