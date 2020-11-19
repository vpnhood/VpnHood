using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class TestInit
    {
        public const string USER_Admin = "admin";
        public const string USER_VpnServer = "vpn_server";
        public const string TEST_PublicServerDns = "publicfoo.test.vphood.com";
        public const string TEST_PublicServerEndPoint = "10.10.10.1";
        public const string TEST_PrivateServerDns = "privatefoo.test.vphood.com";
        public const string TEST_PrivateServerEndPoint = "10.10.10.2";


        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext _)
        {
            App.ConnectionString = "Server=.; initial catalog=Vh; Integrated Security=true;";
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
        }

        public static async Task InitCertificates()
        {
            var certificateControl = TestUtil.CreateCertificateController();
            await certificateControl.Create(TEST_PublicServerEndPoint, $"CN={TEST_PublicServerDns}");
            await certificateControl.Create(TEST_PrivateServerEndPoint, $"CN={TEST_PrivateServerDns}");
        }
    }
}
