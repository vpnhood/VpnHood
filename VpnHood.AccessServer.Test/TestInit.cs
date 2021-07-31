using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.Models;
using VpnHood.Logging;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class TestInit
    {
        public const string USER_Admin = "admin";
        public const string USER_VpnServer = "user_vpn_server";
        public Guid ServerId_1 { get; private set; } = Guid.NewGuid();
        public Guid ServerId_2 { get; private set; } = Guid.NewGuid();
        public string PublicServerDns { get; private set; } = $"publicfoo.{Guid.NewGuid()}.com";
        public string PrivateServerDns { get; private set; } = $"privatefoo.{Guid.NewGuid()}.com";
        public IPEndPoint ServerEndPoint_G1S1 { get; private set; }
        public IPEndPoint ServerEndPoint_G1S2 { get; private set; }
        public IPEndPoint ServerEndPoint_G2S1 { get; private set; }
        public IPEndPoint ServerEndPoint_G2S2 { get; private set; }
        public IPEndPoint ServerEndPoint_New1 { get; private set; }
        public IPEndPoint ServerEndPoint_New2 { get; private set; }
        public IPEndPoint ServerEndPoint_New3 { get; private set; }
        public IPAddress ClientIp1 { get; private set; }
        public IPAddress ClientIp2 { get; private set; }
        public Guid AccessTokenGroup1 { get; private set; }
        public Guid AccessTokenGroup2 { get; private set; }
        public Guid AccountId1 { get; private set; }
        public Guid AccountId2 { get; private set; }

        public static async Task<IPAddress> NewIp()
        {
            VhContext vhContext = new();
            var setting = await vhContext.Settings.FirstOrDefaultAsync();
            if (setting == null)
            {
                setting = new Setting { SettingId = 1 };
                setting.Reserved1 = "1000";
                await vhContext.Settings.AddAsync(setting);
            }
            else
            {
                setting.Reserved1 = (long.Parse(setting.Reserved1) + 1).ToString();
                vhContext.Settings.Update(setting);
            }

            await vhContext.SaveChangesAsync();
            return new IPAddress(long.Parse(setting.Reserved1));
        }

        public static async Task<IPEndPoint> NewEndPoint()
            => new IPEndPoint(await NewIp(), 443);

        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext _)
        {
            VhLogger.IsDiagnoseMode = true;
            App.ConnectionString = "Server=.; initial catalog=Vh2; Integrated Security=true;"; //todo Vh2
            App.AdminUserId = "auth:" + USER_Admin;
            App.VpnServerUserId = "auth:" + USER_VpnServer;
            App.AuthProviderItems = new Settings.AuthProviderItem[]
            {
                new Settings.AuthProviderItem()
                {
                    Schema= "auth",
                    Issuers = new []{"test.vpnhood.com" },
                    NameClaimType= "name",
                    ValidAudiences = new[] { "access.vpnhood.com" },
                    SymmetricSecurityKey = "yVl4m9EdX4EQmcwNWdtMaDD1+k90Wn3oRo6/2Wq2sJY="
                }
            };

            // create account
        }

        public async Task Init()
        {
            ServerEndPoint_G1S1 = await NewEndPoint();
            ServerEndPoint_G1S2 = await NewEndPoint();
            ServerEndPoint_G2S1 = await NewEndPoint();
            ServerEndPoint_G2S2 = await NewEndPoint();
            ServerEndPoint_New1 = await NewEndPoint();
            ServerEndPoint_New2 = await NewEndPoint();
            ServerEndPoint_New3 = await NewEndPoint();
            ClientIp1 = await NewIp();
            ClientIp2 = await NewIp();

            // create Account1
            var accountControl = CreateAccountController();
            var account1 = await accountControl.Create();
            AccountId1 = account1.AccountId;
            AccessTokenGroup1 = account1.AccessTokenGroups.Single(x => x.IsDefault).AccessTokenGroupId;

            var accessTokenGroupController = CreateAccessTokenGroupController();
            AccessTokenGroup2 = (await accessTokenGroupController.Create(AccountId1, $"Group2_{Guid.NewGuid()}")).AccessTokenGroupId;

            // create Account2
            var account2 = await accountControl.Create();
            AccountId2 = account2.AccountId;

            var certificateControl = CreateServerEndPointController();
            await certificateControl.Create(AccountId1, ServerEndPoint_G1S1.ToString(), AccessTokenGroup1, $"CN={PublicServerDns}", true);
            await certificateControl.Create(AccountId1, ServerEndPoint_G1S2.ToString(), AccessTokenGroup1, $"CN={PublicServerDns}");
            await certificateControl.Create(AccountId1, ServerEndPoint_G2S1.ToString(), AccessTokenGroup2, $"CN={PrivateServerDns}", true);
            await certificateControl.Create(AccountId1, ServerEndPoint_G2S2.ToString(), AccessTokenGroup2, $"CN={PrivateServerDns}");

            var accessController = CreateAccessController();
            await accessController.Subscribe(ServerId_1, new() { EnvironmentVersion = Environment.Version });
            await accessController.Subscribe(ServerId_2, new() { EnvironmentVersion = Environment.Version });
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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "<Pending>")]
        private ControllerContext CreateControllerContext(string userId, Guid? accountId = null, Guid? serverId = null)
        {
            DefaultHttpContext httpContext = new();
            ClaimsIdentity claimsIdentity = new(
                new[] {
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim("iss", "auth"),
                    accountId!=null ? new Claim("account_id", accountId?.ToString()) : new Claim("fake_header", "ff")
                });
            httpContext.User = new(claimsIdentity);
            if (serverId != null)
                httpContext.Request.Headers.Add("serverId", serverId.ToString());

            ActionContext actionContext = new(
                httpContext,
                new Microsoft.AspNetCore.Routing.RouteData(),
                new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor());

            return new ControllerContext(actionContext);
        }

        public AccessTokenController CreateAccessTokenController(string userId = USER_Admin)
        {
            var controller = new AccessTokenController(CreateConsoleLogger<AccessTokenController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public ServerEndPointController CreateServerEndPointController(string userId = USER_Admin)
        {
            var controller = new ServerEndPointController(CreateConsoleLogger<ServerEndPointController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public AccountController CreateAccountController(string userId = USER_Admin)
        {
            var controller = new AccountController(CreateConsoleLogger<AccountController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public AccessTokenGroupController CreateAccessTokenGroupController(string userId = USER_Admin)
        {
            var controller = new AccessTokenGroupController(CreateConsoleLogger<AccessTokenGroupController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public AccessController CreateAccessController(string userId = USER_VpnServer, Guid? accountId = null, Guid? serverId = null)
        {
            var controller = new AccessController(CreateConsoleLogger<AccessController>(true))
            {
                ControllerContext = CreateControllerContext(userId, accountId ?? AccountId1, serverId ?? ServerId_1)
            };
            return controller;
        }

        public ServerController CreateServerController(string userId = USER_VpnServer)
        {
            var controller = new ServerController(CreateConsoleLogger<ServerController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public ClientController CreateClientController(string userId = USER_VpnServer)
        {
            var controller = new ClientController(CreateConsoleLogger<ClientController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }
    }
}
