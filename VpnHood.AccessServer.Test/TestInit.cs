using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;
using VpnHood.Logging;

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
        public static readonly IPEndPoint TEST_ServerEndPoint_G1S1 = IPEndPoint.Parse("10.10.10.1:443");
        public static readonly IPEndPoint TEST_ServerEndPoint_G1S2 = IPEndPoint.Parse("10.10.10.2:443");
        public static readonly IPEndPoint TEST_ServerEndPoint_G2S1 = IPEndPoint.Parse("10.10.11.1:443");
        public static readonly IPEndPoint TEST_ServerEndPoint_G2S2 = IPEndPoint.Parse("10.10.11.2:443");
        public static readonly IPEndPoint TEST_ServerEndPoint_New1 = IPEndPoint.Parse("10.10.100.1:443");
        public static readonly IPEndPoint TEST_ServerEndPoint_New2 = IPEndPoint.Parse("10.10.100.2:443");
        public static readonly IPEndPoint TEST_ServerEndPoint_New3 = IPEndPoint.Parse("10.10.100.3:443");
        public static readonly IPAddress TEST_ClientIp1 = IPAddress.Parse("1.1.1.1");
        public static readonly IPAddress TEST_ClientIp2 = IPAddress.Parse("1.1.1.2");

        public static Guid TEST_AccessTokenGroup1 { get; private set; }
        public static Guid TEST_AccessTokenGroup2 { get; private set; }
        public static Guid TEST_AccountId1 { get; private set; }
        public static Guid TEST_AccountId2 { get; private set; }

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

        public static async Task Init()
        {
            // create Account1
            var accountControl = TestHelper.CreateAccountController(accountId: Guid.Empty);
            var account1 = await accountControl.Create();
            TEST_AccountId1 = account1.AccountId;
            TEST_AccessTokenGroup1 = account1.AccessTokenGroups.Single(x => x.IsDefault).AccessTokenGroupId;

            var accessTokenGroupController = TestHelper.CreateAccessTokenGroupController(accountId: account1.AccountId);
            TEST_AccessTokenGroup2 = (await accessTokenGroupController.Create(TestInit.TEST_AccountId1, "Group2")).AccessTokenGroupId;

            // create Account2
            var account2 = await accountControl.Create();
            TEST_AccountId2 = account2.AccountId;


            var certificateControl = TestHelper.CreateServerEndPointController(accountId: account1.AccountId);
            await certificateControl.Create(TEST_AccountId1, TEST_ServerEndPoint_G1S1.ToString(), TEST_AccessTokenGroup1, $"CN={TEST_PublicServerDns}", true);
            await certificateControl.Create(TEST_AccountId1, TEST_ServerEndPoint_G1S2.ToString(), TEST_AccessTokenGroup1, $"CN={TEST_PublicServerDns}");
            await certificateControl.Create(TEST_AccountId1, TEST_ServerEndPoint_G2S1.ToString(), TEST_AccessTokenGroup2, $"CN={TEST_PrivateServerDns}", true);
            await certificateControl.Create(TEST_AccountId1, TEST_ServerEndPoint_G2S2.ToString(), TEST_AccessTokenGroup2, $"CN={TEST_PrivateServerDns}");
        }
    }
}
