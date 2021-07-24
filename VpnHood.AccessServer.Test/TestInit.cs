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
        public const string SERVER_VpnServer1 = "vpn_server_1";
        public const string TEST_PublicServerDns = "publicfoo.test.vphood.com";
        public const string TEST_PrivateServerDns = "privatefoo.test.vphood.com";
        public static readonly int TEST_ServerEndPointGroup1 = 0;
        public static readonly IPEndPoint TEST_ServerEndPoint_G1S1 = IPEndPoint.Parse("10.10.10.1:443");
        public static readonly IPEndPoint TEST_ServerEndPoint_G1S2 = IPEndPoint.Parse("10.10.10.2:443");
        public static readonly int TEST_ServerEndPointGroup2 = 1;
        public static readonly IPEndPoint TEST_ServerEndPoint_G2S1 = IPEndPoint.Parse("10.10.11.1:443");
        public static readonly IPEndPoint TEST_ServerEndPoint_G2S2 = IPEndPoint.Parse("10.10.11.2:443");
        public static readonly int TEST_ServerEndPointGroup_New1 = 2;
        public static readonly IPEndPoint TEST_ServerEndPoint_New1 = IPEndPoint.Parse("10.10.100.1:443");
        public static readonly IPEndPoint TEST_ServerEndPoint_New2 = IPEndPoint.Parse("10.10.100.2:443");
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
            var certificateControl = TestUtil.CreateServerEndPointController();
            await certificateControl.Create(TEST_ServerEndPoint_G1S1.ToString(), $"CN={TEST_PublicServerDns}", TEST_ServerEndPointGroup1);
            await certificateControl.Create(TEST_ServerEndPoint_G1S2.ToString(), $"CN={TEST_PublicServerDns}", TEST_ServerEndPointGroup1);
            await certificateControl.Create(TEST_ServerEndPoint_G2S1.ToString(), $"CN={TEST_PrivateServerDns}", TEST_ServerEndPointGroup2);
            await certificateControl.Create(TEST_ServerEndPoint_G2S2.ToString(), $"CN={TEST_PrivateServerDns}", TEST_ServerEndPointGroup2);
        }
    }
}
