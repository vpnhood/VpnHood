using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.Test.Mock;

namespace VpnHood.AccessServer.Test
{

    public class TestUtil
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

        public static CertificateController CreateCertificateController(string userId = TestInit.USER_Admin)
        {
            var controller = new CertificateController(CreateConsoleLogger<CertificateController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

        public static AccessController CreateAccessController(string userId = TestInit.USER_VpnServer)
        {
            var controller = new AccessController(CreateConsoleLogger<AccessController>(true))
            {
                ControllerContext = CreateControllerContext(userId)
            };
            return controller;
        }

    }
}
