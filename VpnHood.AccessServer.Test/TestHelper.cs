using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Data.SqlClient;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.Test.Mock;

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

        public static ControllerContext CreateControllerContext(string userId)
        {
            ActionContext actionContext = new(
                new MockHttpContext(userId),
                new Microsoft.AspNetCore.Routing.RouteData(),
                new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor());
            return new ControllerContext(actionContext);
        }

        public static AccessTokenController CreateAccessTokenController(string userId = TestInit.USER_Admin)
        {
            var controller = new AccessTokenController(CreateConsoleLogger<AccessTokenController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public static ServerEndPointController CreateServerEndPointController(string userId = TestInit.USER_Admin)
        {
            var controller = new ServerEndPointController(CreateConsoleLogger<ServerEndPointController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public static ServerEndPointGroupController CreateServerEndPointGroupController(string userId = TestInit.USER_Admin)
        {
            var controller = new ServerEndPointGroupController(CreateConsoleLogger<ServerEndPointGroupController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public static AccessController CreateAccessController(string userId = TestInit.USER_VpnServer, Guid? serverId = null)
        {
            var controller = new AccessController(CreateConsoleLogger<AccessController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public static ServerController CreateServerController(string userId = TestInit.USER_VpnServer, Guid? serverId = null)
        {
            var controller = new ServerController(CreateConsoleLogger<ServerController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public static ClientController CreateClientController(string userId = TestInit.USER_VpnServer, Guid? serverId = null) 
        {
            var controller = new ClientController(CreateConsoleLogger<ClientController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }
    }
}
