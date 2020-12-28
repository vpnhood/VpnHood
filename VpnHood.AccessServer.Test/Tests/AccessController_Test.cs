using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Data.SqlClient;
using System.Threading.Tasks;
using System.Transactions;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Services;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class AccessController_Test
    {
        private TransactionScope _trans;

        [TestInitialize()]
        public void Init()
        {
            _trans = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
            TestInit.InitCertificates().Wait();
        }

        [TestCleanup()]
        public void Cleanup()
        {
            _trans.Dispose();
        }

        [TestMethod]
        public async Task GetAccess_Status_Expired()
        {
            var accessTokenController = TestUtil.CreateAccessTokenController();

            // create accessToken
            var accessToken = await accessTokenController.CreatePrivate(tokenName: "private",
                serverEndPoint: TestInit.TEST_PrivateServerEndPoint, maxTraffic: 100, endTime: new DateTime(1900, 1, 1), lifetime: 0, maxClient: 22);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.accessTokenId, ClientIp = "1.1.1.1" };
            var accessController = TestUtil.CreateAccessController();

            var access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access.SentTrafficByteCount);
            Assert.AreEqual(10, access.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Expired, access.StatusCode);
        }

        [TestMethod]
        public async Task GetAccess_Status_TrafficOverflow()
        {
            var accessTokenController = TestUtil.CreateAccessTokenController();

            // create accessToken
            var accessToken = await accessTokenController.CreatePrivate(tokenName: "private",
                serverEndPoint: TestInit.TEST_PrivateServerEndPoint, maxTraffic: 14, endTime: null, lifetime: 0, maxClient: 22);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.accessTokenId, ClientIp = "1.1.1.1" };
            var accessController = TestUtil.CreateAccessController();

            //-----------
            // check: add usage
            //-----------
            var access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access.SentTrafficByteCount);
            Assert.AreEqual(10, access.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.TrafficOverflow, access.StatusCode);
        }

        [TestMethod]
        public async Task GetAccess_Status_No_TrafficOverflow_when_maxTraffic_is_zero()
        {
            var accessTokenController = TestUtil.CreateAccessTokenController();

            // create accessToken
            var accessToken = await accessTokenController.CreatePrivate(tokenName: "private",
                serverEndPoint: TestInit.TEST_PrivateServerEndPoint, maxTraffic: 0, endTime: null, lifetime: 0, maxClient: 22);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.accessTokenId, ClientIp = "1.1.1.1" };
            var accessController = TestUtil.CreateAccessController();

            //-----------
            // check: add usage
            //-----------
            var access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access.SentTrafficByteCount);
            Assert.AreEqual(10, access.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);
        }

        [TestMethod]
        public async Task AddAccessUsage_set_expirationtime_first_use()
        {
            var accessTokenController = TestUtil.CreateAccessTokenController();

            // create token
            var accessToken = await accessTokenController.CreatePrivate(tokenName: "private",
                serverEndPoint: TestInit.TEST_PrivateServerEndPoint, maxTraffic: 100, endTime: null, lifetime: 30, maxClient: 22);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.accessTokenId, ClientIp = "1.1.1.1" };
            var accessController = TestUtil.CreateAccessController();

            //-----------
            // check: add usage
            //-----------
            var access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access.SentTrafficByteCount);
            Assert.AreEqual(10, access.ReceivedTrafficByteCount);
            Assert.IsTrue((access.ExpirationTime.Value - DateTime.Now.AddDays(30)).TotalSeconds < 10);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);
        }

        [TestMethod]
        public async Task GetAccess_should_not_set_expiration_Time()
        {
            var accessTokenController = TestUtil.CreateAccessTokenController();

            // create token
            var accessToken = await accessTokenController.CreatePrivate(tokenName: "private",
                serverEndPoint: TestInit.TEST_PrivateServerEndPoint, maxTraffic: 100, endTime: null, lifetime: 30, maxClient: 22);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.accessTokenId, ClientIp = "1.1.1.1" };
            var accessController = TestUtil.CreateAccessController();
            var access = await accessController.GetAccess(clientIdentity1);
            Assert.IsNull(access.ExpirationTime);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);
        }

        [TestMethod]
        public async Task AddUsage_should_not_retset_expiration_Time()
        {
            var expectedExpirationTime = DateTime.Now.AddDays(10).Date;

            // create token
            var tokenService = await AccessTokenService.CreatePrivate(tokenName: "private",
                serverEndPoint: TestInit.TEST_PrivateServerEndPoint, maxTraffic: 100, endTime: expectedExpirationTime, lifetime: 30, maxClient: 22);

            var clientIdentity1 = new ClientIdentity() { TokenId = tokenService.Id, ClientIp = "1.1.1.1" };
            var accessController = TestUtil.CreateAccessController();
            var access = await accessController.GetAccess(clientIdentity1);
            Assert.AreEqual(expectedExpirationTime, access.ExpirationTime);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);
        }

        [TestMethod]
        public async Task GetAccess_Data()
        {
            // create token
            var tokenService = await AccessTokenService.CreatePrivate(tokenName: "private",
                serverEndPoint: TestInit.TEST_PrivateServerEndPoint, maxTraffic: 100, endTime: new DateTime(2040, 1, 1), lifetime: 0, maxClient: 22);

            var clientIdentity1 = new ClientIdentity() { TokenId = tokenService.Id, ClientIp = "1.1.1.1" };
            var accessController = TestUtil.CreateAccessController();
            var access = await accessController.GetAccess(clientIdentity1);

            Assert.IsNotNull(access.AccessId);
            Assert.AreEqual(TestInit.TEST_PrivateServerEndPoint, access.ServerEndPoint);
            Assert.AreEqual(new DateTime(2040, 1, 1), access.ExpirationTime);
            Assert.AreEqual(22, access.MaxClientCount);
            Assert.AreEqual(100, access.MaxTrafficByteCount);
            Assert.AreEqual(0, access.ReceivedTrafficByteCount);
            Assert.AreEqual(0, access.SentTrafficByteCount);
            Assert.IsNotNull(access.Secret);
        }

        [TestMethod]
        public async Task AddUsage_Private()
        {
            // create token
            var tokenService = await AccessTokenService.CreatePrivate(tokenName: "private",
                serverEndPoint: TestInit.TEST_PrivateServerEndPoint, maxTraffic: 100, endTime: new DateTime(2040, 1, 1), lifetime: 0, maxClient: 2);

            var clientIdentity1 = new ClientIdentity() { TokenId = tokenService.Id, ClientIp = "1.1.1.1" };
            var clientIdentity2 = new ClientIdentity() { TokenId = tokenService.Id, ClientIp = "1.1.1.2" };

            var accessController = TestUtil.CreateAccessController();

            //--------------
            // check: zero usage
            //--------------
            var access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 0,
                ReceivedTrafficByteCount = 0
            });
            Assert.AreEqual(0, access.SentTrafficByteCount);
            Assert.AreEqual(0, access.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

            //-----------
            // check: add usage
            //-----------
            access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access.SentTrafficByteCount);
            Assert.AreEqual(10, access.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

            // again
            access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });

            Assert.AreEqual(10, access.SentTrafficByteCount);
            Assert.AreEqual(20, access.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

            //-----------
            // check: add usage for client 2
            //-----------
            access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity2,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });

            Assert.AreEqual(15, access.SentTrafficByteCount);
            Assert.AreEqual(30, access.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);


            //-------------
            // check: GetAccess should return same result
            //-------------
            var access2 = await accessController.GetAccess(clientIdentity2);
            Assert.AreEqual(JsonConvert.SerializeObject(access), JsonConvert.SerializeObject(access2));
        }

        [TestMethod]
        public async Task AddUsage_Public()
        {
            // create token
            var tokenService = await AccessTokenService.CreatePublic(tokenName: "public",
                serverEndPoint: TestInit.TEST_PublicServerEndPoint, maxTraffic: 100);

            var clientIdentity1 = new ClientIdentity() { TokenId = tokenService.Id, ClientIp = "1.1.1.1" };
            var clientIdentity2 = new ClientIdentity() { TokenId = tokenService.Id, ClientIp = "1.1.1.2" };

            var accessController = TestUtil.CreateAccessController();

            //--------------
            // check: zero usage
            //--------------
            var access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 0,
                ReceivedTrafficByteCount = 0
            });
            Assert.AreEqual(0, access.SentTrafficByteCount);
            Assert.AreEqual(0, access.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

            //-----------
            // check: add usage
            //-----------
            access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access.SentTrafficByteCount);
            Assert.AreEqual(10, access.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

            // again
            access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });

            Assert.AreEqual(10, access.SentTrafficByteCount);
            Assert.AreEqual(20, access.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

            //-----------
            // check: add usage for client 2
            //-----------
            access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity2,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });

            Assert.AreEqual(5, access.SentTrafficByteCount);
            Assert.AreEqual(10, access.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

            //-------------
            // check: add usage to client 1 after cycle
            //-------------

            //remove last cycle
            var curentCycleId = PublicCycleService.GetCurrentCycleId();
            using var sqlConnection = App.OpenConnection();
            await sqlConnection.ExecuteAsync(@$"DELETE FROM {PublicCycle.Table_} WHERE {PublicCycle.publicCycleId_} = @{nameof(curentCycleId)}", new
            {
                curentCycleId
            });

            access = await accessController.AddUsage(new AddUsageParams()
            {
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access.SentTrafficByteCount);
            Assert.AreEqual(10, access.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

            //-------------
            // check: GetAccess should return same result
            //-------------
            var access2 = await accessController.GetAccess(clientIdentity1);
            Assert.AreEqual(JsonConvert.SerializeObject(access), JsonConvert.SerializeObject(access2));

        }

        [TestMethod]
        public async Task GetAccessUsage_Public()
        {
            var accessTokenController = TestUtil.CreateAccessTokenController();
            var accessController = TestUtil.CreateAccessController();

            var accessToken = await accessTokenController.CreatePublic(tokenName: "PublicName",
                serverEndPoint: TestInit.TEST_PublicServerEndPoint, maxTraffic: 10);

            var clientIdentity = new ClientIdentity() { TokenId = accessToken.accessTokenId, ClientIp = "1.1.1.1" };
            var accessUsage = await accessController.GetAccess(clientIdentity: clientIdentity);
            Assert.AreEqual(0, accessUsage.ReceivedTrafficByteCount);
            Assert.AreEqual(0, accessUsage.SentTrafficByteCount);

            accessUsage = await accessController.GetAccess(clientIdentity: clientIdentity);
            Assert.AreEqual(0, accessUsage.ReceivedTrafficByteCount);
            Assert.AreEqual(0, accessUsage.SentTrafficByteCount);

            // GetAccess should return same result
            var clientInfo2 = await accessController.AddUsage(new AddUsageParams() { ClientIdentity = clientIdentity });
            Assert.AreEqual(JsonConvert.SerializeObject(accessUsage), JsonConvert.SerializeObject(clientInfo2));
        }

        [TestMethod]
        public async Task AddAccessUsage_Public()
        {
            var accessTokenController = TestUtil.CreateAccessTokenController();
            var accessController = TestUtil.CreateAccessController();

            // create token
            var accssToken = await accessTokenController.CreatePublic(tokenName: "public",
                serverEndPoint: TestInit.TEST_PublicServerEndPoint, maxTraffic: 100);

            var clientIdentity1 = new ClientIdentity() { TokenId = accssToken.accessTokenId, ClientIp = "1.1.1.1" };
            var clientIdentity2 = new ClientIdentity() { TokenId = accssToken.accessTokenId, ClientIp = "1.1.1.2" };
            var tokenService1 = AccessTokenService.FromId(clientIdentity1.TokenId);
            var tokenService2 = AccessTokenService.FromId(clientIdentity2.TokenId);

            //--------------
            // check: zero usage
            //--------------
            var accessUsage = await accessController.AddUsage(new AddUsageParams() { ClientIdentity = clientIdentity1, SentTrafficByteCount = 0, ReceivedTrafficByteCount = 0 });
            Assert.AreEqual(0, accessUsage.SentTrafficByteCount);
            Assert.AreEqual(0, accessUsage.ReceivedTrafficByteCount);

            var accessUsageEx = await tokenService1.GetAccessUsage(clientIdentity1.ClientIp);
            Assert.AreEqual(0, accessUsageEx.totalSentTraffic);
            Assert.AreEqual(0, accessUsageEx.totalReceivedTraffic);

            //-----------
            // check: add usage
            //-----------
            accessUsage = await accessController.AddUsage(new AddUsageParams() { ClientIdentity = clientIdentity1, SentTrafficByteCount = 5, ReceivedTrafficByteCount = 10 });
            Assert.AreEqual(5, accessUsage.SentTrafficByteCount);
            Assert.AreEqual(10, accessUsage.ReceivedTrafficByteCount);

            accessUsageEx = await tokenService1.GetAccessUsage(clientIdentity1.ClientIp);
            Assert.AreEqual(5, accessUsageEx.totalSentTraffic);
            Assert.AreEqual(10, accessUsageEx.totalReceivedTraffic);

            // again
            accessUsage = await accessController.AddUsage(new AddUsageParams() { ClientIdentity = clientIdentity1, SentTrafficByteCount = 5, ReceivedTrafficByteCount = 10 });

            Assert.AreEqual(10, accessUsage.SentTrafficByteCount);
            Assert.AreEqual(20, accessUsage.ReceivedTrafficByteCount);

            accessUsageEx = await tokenService1.GetAccessUsage(clientIdentity1.ClientIp);
            Assert.AreEqual(10, accessUsageEx.totalSentTraffic);
            Assert.AreEqual(20, accessUsageEx.totalReceivedTraffic);

            //-----------
            // check: add usage for client 2
            //-----------
            accessUsage = await accessController.AddUsage(new AddUsageParams() { ClientIdentity = clientIdentity2, SentTrafficByteCount = 2, ReceivedTrafficByteCount = 3 });
            Assert.AreEqual(2, accessUsage.SentTrafficByteCount);
            Assert.AreEqual(3, accessUsage.ReceivedTrafficByteCount);

            accessUsageEx = await tokenService2.GetAccessUsage(clientIdentity2.ClientIp);
            Assert.AreEqual(2, accessUsageEx.totalSentTraffic);
            Assert.AreEqual(3, accessUsageEx.totalReceivedTraffic);

            //-------------
            // check : add usage to client 1 after cycle
            //-------------

            //remove last cycle
            var curentCycleId = PublicCycleService.GetCurrentCycleId();
            using var sqlConnection = App.OpenConnection();
            sqlConnection.Execute(@$"DELETE FROM {PublicCycle.Table_} WHERE {PublicCycle.publicCycleId_} = @{nameof(curentCycleId)}", new { curentCycleId });

            accessUsage = await accessController.AddUsage(new AddUsageParams() { ClientIdentity = clientIdentity1, SentTrafficByteCount = 5, ReceivedTrafficByteCount = 10 });
            Assert.AreEqual(5, accessUsage.SentTrafficByteCount);
            Assert.AreEqual(10, accessUsage.ReceivedTrafficByteCount);

            accessUsageEx = await tokenService1.GetAccessUsage(clientIdentity2.ClientIp);
            Assert.AreEqual(2, accessUsageEx.totalSentTraffic);
            Assert.AreEqual(3, accessUsageEx.totalReceivedTraffic);

            //-------------
            // check : GetAccess should return same result
            //-------------
            var accessUsage2 = await accessController.GetAccess(clientIdentity1);
            Assert.AreEqual(JsonConvert.SerializeObject(accessUsage), JsonConvert.SerializeObject(accessUsage2));
        }

        [TestMethod]
        public async Task UsageLog_Inserted()
        {
            var accessTokenController = TestUtil.CreateAccessTokenController();
            var accessController = TestUtil.CreateAccessController();

            // create token
            var accssToken = await accessTokenController.CreatePublic(tokenName: "public",
                serverEndPoint: TestInit.TEST_PublicServerEndPoint, maxTraffic: 100);

            var clientIdentity1 = new ClientIdentity() { TokenId = accssToken.accessTokenId, ClientIp = "1.1.1.1", ClientId = Guid.NewGuid() };

            //-----------
            // check: add usage
            //-----------
            await accessController.AddUsage(new AddUsageParams() { ClientIdentity = clientIdentity1, SentTrafficByteCount = 10051, ReceivedTrafficByteCount = 20051 });
            await accessController.AddUsage(new AddUsageParams() { ClientIdentity = clientIdentity1, SentTrafficByteCount = 20, ReceivedTrafficByteCount = 30 });


            // query database for usage
            var sql = @$"
                SELECT 
                       UL.{UsageLog.usageLogId_}, 
                       UL.{UsageLog.accessTokenId_}, 
                       UL.{UsageLog.clientId_}, 
                       UL.{UsageLog.clientIp_}, 
                       UL.{UsageLog.sentTraffic_}, 
                       UL.{UsageLog.receivedTraffic_}, 
                       UL.{UsageLog.cycleSentTraffic_}, 
                       UL.{UsageLog.cycleReceivedTraffic_},
                       UL.{UsageLog.totalSentTraffic_}, 
                       UL.{UsageLog.totalReceivedTraffic_}
                FROM {UsageLog.Table_} AS UL
                WHERE UL.{UsageLog.sentTraffic_} = 20
                ";

            using var sqlConnection = App.OpenConnection();
            var ret = await sqlConnection.QuerySingleAsync<UsageLog>(sql);

            Assert.AreEqual(clientIdentity1.TokenId, ret.accessTokenId);
            Assert.AreEqual(clientIdentity1.ClientId, ret.clientId);
            Assert.AreEqual(clientIdentity1.ClientIp, ret.clientIp);
            Assert.AreEqual(20, ret.sentTraffic);
            Assert.AreEqual(30, ret.receivedTraffic);
            Assert.AreEqual(10071, ret.cycleSentTraffic);
            Assert.AreEqual(20081, ret.cycleReceivedTraffic);
            Assert.AreEqual(10071, ret.totalSentTraffic);
            Assert.AreEqual(20081, ret.totalReceivedTraffic);
        }

    }
}
