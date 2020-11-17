using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using VpnHood.AccessServer.Test.Mock;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class TestInit
    {
        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext _)
        {
            App.ConnectionString = "Server=.; initial catalog=Vh; Integrated Security=true;";
            App.AgentUserId = "auth:admin";
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
        }

        public static ControllerContext CreateControllerContext()
        {
            ActionContext actionContext = new(
                new MockHttpContext(),
                new Microsoft.AspNetCore.Routing.RouteData(),
                new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor());
            return new ControllerContext(actionContext);
        }
    }
}
