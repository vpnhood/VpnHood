using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Data.SqlClient;
using System.Net;
using System.Threading.Tasks;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public static class TestInit
    {
        public const string USER_Admin = "admin";
        public const string USER_VpnServer = "user_vpn_server";
        public static readonly Guid TEST_ServerId_1 = Guid.Parse("{E89F5BF3-FB06-41E1-9F8C-7A613EDA6C22}");
        public static readonly Guid TEST_ServerId_2 = Guid.Parse("{E89F5BF3-FB06-41E1-9F8C-7A613EDA6C23}");
        public static readonly string TEST_PublicServerDns = "publicfoo.test.vphood.com";
        public static readonly string TEST_PrivateServerDns = "privatefoo.test.vphood.com";
        public static readonly Guid TEST_ServerEndPointGroup1 = Guid.Parse("{3A831A41-C84E-48D4-B3A5-A7495FCEDCDC}");
        public static readonly IPEndPoint TEST_ServerEndPoint_G1S1 = IPEndPoint.Parse("10.10.10.1:443");
        public static readonly IPEndPoint TEST_ServerEndPoint_G1S2 = IPEndPoint.Parse("10.10.10.2:443");
        public static readonly Guid TEST_ServerEndPointGroup2 = Guid.Parse("{3A831A41-C84E-48D4-B3A5-A7495FCEDCDD}");
        public static readonly IPEndPoint TEST_ServerEndPoint_G2S1 = IPEndPoint.Parse("10.10.11.1:443");
        public static readonly IPEndPoint TEST_ServerEndPoint_G2S2 = IPEndPoint.Parse("10.10.11.2:443");
        public static readonly IPEndPoint TEST_ServerEndPointId_New1 = IPEndPoint.Parse("10.10.100.1:443");
        public static readonly IPEndPoint TEST_ServerEndPointId_New2 = IPEndPoint.Parse("10.10.100.2:443");
        public static readonly IPEndPoint TEST_ServerEndPointId_New3 = IPEndPoint.Parse("10.10.100.3:443");
        public static readonly IPAddress TEST_ClientIp1 = IPAddress.Parse("1.1.1.1");
        public static readonly IPAddress TEST_ClientIp2 = IPAddress.Parse("1.1.1.2");

        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext _)
        {
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
        }

        public static async Task InitCertificates()
        {
            var certificateControl = TestHelper.CreateServerEndPointController();
            await certificateControl.Create(TEST_ServerEndPoint_G1S1.ToString(), TEST_ServerEndPointGroup1, $"CN={TEST_PublicServerDns}", true);
            await certificateControl.Create(TEST_ServerEndPoint_G1S2.ToString(), TEST_ServerEndPointGroup1, $"CN={TEST_PublicServerDns}");
            await certificateControl.Create(TEST_ServerEndPoint_G2S1.ToString(), TEST_ServerEndPointGroup2, $"CN={TEST_PrivateServerDns}", true);
            await certificateControl.Create(TEST_ServerEndPoint_G2S2.ToString(), TEST_ServerEndPointGroup2, $"CN={TEST_PrivateServerDns}");
        }
    }
}
