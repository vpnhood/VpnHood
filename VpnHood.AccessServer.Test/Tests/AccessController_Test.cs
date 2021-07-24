using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Transactions;
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
            var accessToken = await accessTokenController.Create(endTime: new DateTime(1900, 1, 1));

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };
            var accessController = TestUtil.CreateAccessController();

            var access = await accessController.AddUsage(new UsageParams()
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
            var accessToken = await accessTokenController.Create(maxTraffic: 14);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };
            var accessController = TestUtil.CreateAccessController();

            //-----------
            // check: add usage
            //-----------
            var access = await accessController.AddUsage(new UsageParams()
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
            var accessToken = await accessTokenController.Create(maxTraffic: 0);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };
            var accessController = TestUtil.CreateAccessController();

            //-----------
            // check: add usage
            //-----------
            var access = await accessController.AddUsage(new UsageParams()
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
            var accessToken = await accessTokenController.Create(endTime: null, lifetime: 30);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };
            var accessController = TestUtil.CreateAccessController();

            //-----------
            // check: add usage
            //-----------
            var access = await accessController.AddUsage(new UsageParams()
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
        public async Task GetAccess_should_not_set_expiration_time_when_endTime_is_not_set()
        {
            var accessTokenController = TestUtil.CreateAccessTokenController();

            // create token
            var accessToken = await accessTokenController.Create(endTime: null, lifetime: 30);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientIp = TestInit.TEST_ClientIp1 };
            var accessController = TestUtil.CreateAccessController();
            var access = await accessController.GetAccess(new AccessParams { ClientIdentity = clientIdentity1, ServerEndPoint = TestInit.TEST_ServerEndPoint_G2S1 });
            Assert.IsNull(access.ExpirationTime);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);
        }

        [TestMethod]
        public async Task AddUsage_should_not_reset_expiration_Time()
        {
            var expectedExpirationTime = DateTime.Now.AddDays(10).Date;

            // create token
            var tokenService = await AccessTokenService.Create(endTime: expectedExpirationTime, lifetime: 30);

            var clientIdentity1 = new ClientIdentity() { TokenId = tokenService.Id, ClientId = Guid.NewGuid() };
            var accessController = TestUtil.CreateAccessController();
            var access = await accessController.GetAccess(new AccessParams { ClientIdentity = clientIdentity1, ServerEndPoint = TestInit.TEST_ServerEndPoint_G2S1 });
            Assert.AreEqual(expectedExpirationTime, access.ExpirationTime);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);
        }

        [TestMethod]
        public async Task GetAccess()
        {
            // create token
            var tokenService = await AccessTokenService.Create(maxTraffic: 100, endTime: new DateTime(2040, 1, 1), lifetime: 0, maxClient: 22);

            var clientId = Guid.NewGuid();
            var beforeUpdate = DateTime.Now;
            var clientIdentity = new ClientIdentity() { TokenId = tokenService.Id, ClientId = clientId, ClientIp = TestInit.TEST_ClientIp1, UserAgent = "userAgent1", ClientVersion = "1.0.0" };
            var accessParam = new AccessParams { ClientIdentity = clientIdentity, ServerEndPoint = TestInit.TEST_ServerEndPoint_G2S1 };
            var accessController = TestUtil.CreateAccessController();
            var access = await accessController.GetAccess(accessParam);

            Assert.IsNotNull(access.AccessId);
            Assert.AreEqual(new DateTime(2040, 1, 1), access.ExpirationTime);
            Assert.AreEqual(22, access.MaxClientCount);
            Assert.AreEqual(100, access.MaxTrafficByteCount);
            Assert.AreEqual(0, access.ReceivedTrafficByteCount);
            Assert.AreEqual(0, access.SentTrafficByteCount);
            Assert.IsNotNull(access.Secret);

            // check Client id is created 
            var client = await ClientService.FromId(clientIdentity.ClientId).Get();
            Assert.AreEqual(clientIdentity.UserAgent, client.UserAgent);
            Assert.AreEqual(clientIdentity.ClientVersion, client.ClientVersion);
            Assert.IsTrue(client.LastConnectTime >= beforeUpdate);

            // check update
            beforeUpdate = DateTime.Now;
            clientIdentity = new ClientIdentity() { TokenId = tokenService.Id, ClientId = clientId, ClientIp = TestInit.TEST_ClientIp2, UserAgent = "userAgent2", ClientVersion = "2.0.0" };
            accessParam = new AccessParams { ClientIdentity = clientIdentity, ServerEndPoint = TestInit.TEST_ServerEndPoint_G2S1 };
            await accessController.GetAccess(accessParam);
            Assert.AreEqual(clientIdentity.UserAgent, client.UserAgent);
            Assert.AreEqual(clientIdentity.ClientVersion, client.ClientVersion);
            Assert.IsTrue(client.LastConnectTime >= beforeUpdate);
        }

        [TestMethod]
        public async Task GetAccess_Data_Unauthorized_EndPoint()
        {
            // create first public token
            var accessController = TestUtil.CreateAccessController();
            var tokenService = await AccessTokenService.Create(serverEndPointGroupId: TestInit.TEST_ServerEndPointGroup1);
            var tokenId = tokenService.Id;

            //-----------
            // check: access should grant to public token 1 by another public endpoint
            //-----------
            var access = await accessController.GetAccess(new AccessParams { ClientIdentity = new ClientIdentity { ClientId = Guid.NewGuid(), TokenId = tokenId }, ServerEndPoint = TestInit.TEST_ServerEndPoint_G1S2 });
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

            //-----------
            // check: access should not grant to public token 1 by private server endpoint
            //-----------
            access = await accessController.GetAccess(new AccessParams { ClientIdentity = new ClientIdentity { ClientId = Guid.NewGuid(), TokenId = tokenId }, ServerEndPoint = TestInit.TEST_ServerEndPoint_G2S1 });
            Assert.AreEqual(AccessStatusCode.Error, access.StatusCode);

        }

        [TestMethod]
        public async Task AddUsage_Public()
        {
            // create token
            var tokenService = await AccessTokenService.Create();
            var clientIdentity1 = new ClientIdentity() { TokenId = tokenService.Id, ClientId = Guid.NewGuid() };
            var clientIdentity2 = new ClientIdentity() { TokenId = tokenService.Id, ClientId = Guid.NewGuid() };
            var tokenService1 = AccessTokenService.FromId(clientIdentity1.TokenId);
            var tokenService2 = AccessTokenService.FromId(clientIdentity2.TokenId);

            var accessController = TestUtil.CreateAccessController();
            var access1 = await accessController.GetAccess( new AccessParams { ClientIdentity = clientIdentity1, ServerEndPoint = TestInit.TEST_ServerEndPoint_G1S1 });

            //--------------
            // check: zero usage
            //--------------
            access1 = await accessController.AddUsage(new UsageParams()
            {
                AccessId = access1.AccessId,
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 0,
                ReceivedTrafficByteCount = 0
            });
            Assert.AreEqual(0, access1.SentTrafficByteCount);
            Assert.AreEqual(0, access1.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access1.StatusCode);

            var accessUsageEx = await tokenService1.GetAccessUsage(clientIdentity1.ClientIp.ToString());
            Assert.AreEqual(0, accessUsageEx.TotalSentTraffic);
            Assert.AreEqual(0, accessUsageEx.TotalReceivedTraffic);

            //-----------
            // check: add usage
            //-----------
            access1 = await accessController.AddUsage(new UsageParams()
            {
                AccessId = access1.AccessId,
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access1.SentTrafficByteCount);
            Assert.AreEqual(10, access1.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access1.StatusCode);

            accessUsageEx = await tokenService1.GetAccessUsage(clientIdentity1.ClientIp.ToString());
            Assert.AreEqual(5, accessUsageEx.TotalSentTraffic);
            Assert.AreEqual(10, accessUsageEx.TotalReceivedTraffic);

            // again
            access1 = await accessController.AddUsage(new UsageParams()
            {
                AccessId = access1.AccessId,
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });

            Assert.AreEqual(10, access1.SentTrafficByteCount);
            Assert.AreEqual(20, access1.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access1.StatusCode);

            accessUsageEx = await tokenService1.GetAccessUsage(clientIdentity1.ClientIp.ToString());
            Assert.AreEqual(10, accessUsageEx.TotalSentTraffic);
            Assert.AreEqual(20, accessUsageEx.TotalReceivedTraffic);
            
            //-----------
            // check: add usage for client 2
            //-----------
            var access2 = await accessController.GetAccess(new AccessParams { ClientIdentity = clientIdentity2, ServerEndPoint = TestInit.TEST_ServerEndPoint_G1S1 });
            access2 = await accessController.AddUsage(new UsageParams()
            {
                AccessId = access2.AccessId,
                ClientIdentity = clientIdentity2,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });

            Assert.AreEqual(5, access2.SentTrafficByteCount);
            Assert.AreEqual(10, access2.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access2.StatusCode);

            accessUsageEx = await tokenService2.GetAccessUsage(clientIdentity2.ClientIp.ToString());
            Assert.AreEqual(2, accessUsageEx.TotalSentTraffic);
            Assert.AreEqual(3, accessUsageEx.TotalReceivedTraffic);
            //-------------
            // check: add usage to client 1 after cycle
            //-------------

            //remove last cycle
            var curentCycleId = PublicCycleService.GetCurrentCycleId();
            using VhContext vhContext = new();
            vhContext.PublicCycles.RemoveRange(vhContext.PublicCycles.Where(e => e.PublicCycleId == curentCycleId));

            vhContext.PublicCycles.RemoveRange();
            using var sqlConnection = App.OpenConnection();
            await sqlConnection.ExecuteAsync(@$"DELETE FROM {PublicCycle.Table_} WHERE {PublicCycle.publicCycleId_} = @{nameof(curentCycleId)}", new
            {
                curentCycleId
            });

            access1 = await accessController.AddUsage(new UsageParams()
            {
                AccessId = access1.AccessId,
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access1.SentTrafficByteCount);
            Assert.AreEqual(10, access1.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access1.StatusCode);

            accessUsageEx = await tokenService1.GetAccessUsage(clientIdentity2.ClientIp.ToString());
            Assert.AreEqual(2, accessUsageEx.totalSentTraffic);
            Assert.AreEqual(3, accessUsageEx.totalReceivedTraffic);

            //-------------
            // check: GetAccess should return same result
            //-------------
            var access1B = await accessController.GetAccess( new AccessParams { ClientIdentity = clientIdentity1, ServerEndPoint = TestInit.TEST_ServerEndPoint_G1S1 });
            Assert.AreEqual(JsonConvert.SerializeObject(access1), JsonConvert.SerializeObject(access1B));
        }

        [TestMethod]
        public async Task GetCertificateData()
        {
            // create new endPoint
            var dnsName = "fifoo.com";
            var endPointController = TestUtil.CreateServerEndPointController();
            await endPointController.Create(serverEndPoint: TestInit.TEST_ServerEndPoint_New1.ToString(), subjectName: dnsName, TestInit.TEST_ServerEndPointGroup_New1);

            // check serverId is null
            ServerEndPointService serverEndPointService = ServerEndPointService.FromId(TestInit.TEST_ServerEndPoint_New1.ToString());
            var serverEndPoint = await serverEndPointService.Get();
            Assert.IsNull(serverEndPoint.serverId);

            var accessController = TestUtil.CreateAccessController();
            var certBuffer = await accessController.GetSslCertificateData(TestInit.TEST_ServerEndPoint_New1.ToString());
            var certificate = new X509Certificate2(certBuffer);
            Assert.AreEqual(dnsName, certificate.GetNameInfo(X509NameType.DnsName, false));

            //-----------
            // check: check serverId is set
            //-----------
            serverEndPoint = await serverEndPointService.Get();
            ServerService serverService = serverService.FromExternalId(TestInit.SERVER_VpnServer1);
            Server server = serverService.Get();
            TestInit.SERVER_VpnServer1
            Assert.AreEqual(server.serverId, serverEndPoint.serverId);

            //-----------
            // check: check not found
            //-----------
            try
            {
                await accessController.GetSslCertificateData(TestInit.TEST_ServerEndPoint_New2.ToString());
                Assert.Fail("KeyNotFoundException expected!");
            }
            catch (KeyNotFoundException) { }
        }

        [TestMethod]
        public async Task SendServerStatus()
        {
            var serverName = Guid.NewGuid().ToString();
            var accessController = TestUtil.CreateAccessController(serverName: serverName);
            await accessController.SendServerStatus(new ServerStatus { SessionCount = 10 });

            ServerService serverService = serverService.FromExternalId(serverName);
            Server server = serverService.Get();
            Assert.AreEqual(server.lastSessionCount, 10);

        }

        [TestMethod]
        public async Task UsageLog_Inserted()
        {
            var accessTokenController = TestUtil.CreateAccessTokenController();
            var accessController = TestUtil.CreateAccessController();

            // create token
            var accssToken = await accessTokenController.Create();
            var clientIdentity1 = new ClientIdentity() { TokenId = accssToken.accessTokenId, ClientIp = TestInit.TEST_ClientIp1, ClientId = Guid.NewGuid(), ClientVersion = "2.0.2.0", UserAgent = "userAgent1" };

            //-----------
            // check: add usage
            //-----------
            await accessController.AddUsage(new UsageParams() { ClientIdentity = clientIdentity1, SentTrafficByteCount = 10051, ReceivedTrafficByteCount = 20051 });
            await accessController.AddUsage(new UsageParams() { ClientIdentity = clientIdentity1, SentTrafficByteCount = 20, ReceivedTrafficByteCount = 30 });

            // query database for usage
            var sql = @$"
                SELECT 
                       UL.{UsageLog.usageLogId_}, 
                       UL.{UsageLog.accessTokenId_}, 
                       UL.{UsageLog.clientId_}, 
                       UL.{UsageLog.clientIp_}, 
                       UL.{UsageLog.clientVersion_}, 
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
            Assert.AreEqual(clientIdentity1.ClientIp.ToString(), ret.clientIp);
            Assert.AreEqual(clientIdentity1.ClientVersion, ret.clientVersion);
            Assert.AreEqual(20, ret.sentTraffic);
            Assert.AreEqual(30, ret.receivedTraffic);
            Assert.AreEqual(10071, ret.cycleSentTraffic);
            Assert.AreEqual(20081, ret.cycleReceivedTraffic);
            Assert.AreEqual(10071, ret.totalSentTraffic);
            Assert.AreEqual(20081, ret.totalReceivedTraffic);

            // check client id is created
            var client = await ClientService.FromId(clientIdentity1.ClientId).Get();
            Assert.AreEqual(clientIdentity1.UserAgent, client.userAgent);
        }
    }
}
