using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Data.SqlClient;
using System.Security.Claims;
using VpnHood.AccessServer.Controllers;

namespace VpnHood.AccessServer.Test
{
    public class TestHelper
    {
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

        public static ControllerContext CreateControllerContext(string userId, Guid? accountId, Guid? serverId = null)
        {
            if (accountId == null) accountId = TestInit.TEST_AccountId1;

            DefaultHttpContext httpContext = new();
            ClaimsIdentity claimsIdentity = new(
                new[] {
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim("iss", "auth"),
                    new Claim("account_id", accountId.ToString())
                });
            httpContext.User = new(claimsIdentity);
            httpContext.Request.Headers.Add("serverId", serverId.ToString());

            ActionContext actionContext = new(
                httpContext,
                new Microsoft.AspNetCore.Routing.RouteData(),
                new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor());

            return new ControllerContext(actionContext);
        }

        public static AccessTokenController CreateAccessTokenController(string userId = TestInit.USER_Admin, Guid? accountId = null)
        {
            var controller = new AccessTokenController(CreateConsoleLogger<AccessTokenController>(true))
            {
                ControllerContext = CreateControllerContext(userId, accountId)
            };
            return controller;
        }

        public static ServerEndPointController CreateServerEndPointController(string userId = TestInit.USER_Admin, Guid? accountId = null)
        {
            var controller = new ServerEndPointController(CreateConsoleLogger<ServerEndPointController>(true))
            {
                ControllerContext = CreateControllerContext(userId, accountId)
            };
            return controller;
        }

        public static AccountController CreateAccountController(string userId = TestInit.USER_Admin, Guid? accountId = null)
        {
            var controller = new AccountController(CreateConsoleLogger<AccountController>(true))
            {
                ControllerContext = CreateControllerContext(userId, accountId)
            };
            return controller;
        }

        public static ServerEndPointGroupController CreateServerEndPointGroupController(string userId = TestInit.USER_Admin, Guid? accountId = null)
        {
            var controller = new ServerEndPointGroupController(CreateConsoleLogger<ServerEndPointGroupController>(true))
            {
                ControllerContext = CreateControllerContext(userId, accountId)
            };
            return controller;
        }

        public static AccessController CreateAccessController(string userId = TestInit.USER_VpnServer, Guid? accountId = null, Guid? serverId = null)
        {
            var controller = new AccessController(CreateConsoleLogger<AccessController>(true))
            {
                ControllerContext = CreateControllerContext(userId, accountId, serverId)
            };
            return controller;
        }

        public static ServerController CreateServerController(string userId = TestInit.USER_VpnServer, Guid? accountId = null)
        {
            var controller = new ServerController(CreateConsoleLogger<ServerController>(true))
            {
                ControllerContext = CreateControllerContext(userId, accountId)
            };
            return controller;
        }

        public static ClientController CreateClientController(string userId = TestInit.USER_VpnServer, Guid? accountId = null)
        {
            var controller = new ClientController(CreateConsoleLogger<ClientController>(true))
            {
                ControllerContext = CreateControllerContext(userId, accountId)
            };
            return controller;
        }
    }
}
