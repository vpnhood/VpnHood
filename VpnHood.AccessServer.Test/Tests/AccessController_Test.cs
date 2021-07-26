using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Transactions;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.Models;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class AccessController_Test
    {
        private TransactionScope _trans;
        private VhContext _vhContext;

        [TestInitialize()]
        public void Init()
        {
            _trans = new (TransactionScopeAsyncFlowOption.Enabled);
            _vhContext = new();
            TestInit.InitCertificates().Wait();
        }

        [TestCleanup()]
        public void Cleanup()
        {
            _trans.Dispose();
            _vhContext.Dispose();
        }

        [TestMethod]
        public async Task GetAccess_Status_Expired()
        {
            var accessTokenController = TestHelper.CreateAccessTokenController();

            // create accessToken
            var accessToken = await accessTokenController.Create(endTime: new DateTime(1900, 1, 1));

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };
            var accessController = TestHelper.CreateAccessController();

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
            var accessTokenController = TestHelper.CreateAccessTokenController();

            // create accessToken
            var accessToken = await accessTokenController.Create(maxTraffic: 14);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };
            var accessController = TestHelper.CreateAccessController();

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
            var accessTokenController = TestHelper.CreateAccessTokenController();

            // create accessToken
            var accessToken = await accessTokenController.Create(maxTraffic: 0);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };
            var accessController = TestHelper.CreateAccessController();

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
            var accessTokenController = TestHelper.CreateAccessTokenController();

            // create token
            var accessToken = await accessTokenController.Create(endTime: null, lifetime: 30);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };
            var accessController = TestHelper.CreateAccessController();

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
            var accessTokenController = TestHelper.CreateAccessTokenController();

            // create token
            var accessToken = await accessTokenController.Create(endTime: null, lifetime: 30);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientIp = TestInit.TEST_ClientIp1 };
            var accessController = TestHelper.CreateAccessController();
            var access = await accessController.GetAccess(new AccessParams { ClientIdentity = clientIdentity1, RequestEndPoint = TestInit.TEST_ServerEndPoint_G2S1 });
            Assert.IsNull(access.ExpirationTime);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);
        }

        [TestMethod]
        public async Task AddUsage_should_not_reset_expiration_Time()
        {
            var expectedExpirationTime = DateTime.Now.AddDays(10).Date;

            // create token
            var accessTokenController = TestHelper.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(endTime: expectedExpirationTime, lifetime: 30);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };
            var accessController = TestHelper.CreateAccessController();
            var access = await accessController.GetAccess(new AccessParams { ClientIdentity = clientIdentity1, RequestEndPoint = TestInit.TEST_ServerEndPoint_G2S1 });
            Assert.AreEqual(expectedExpirationTime, access.ExpirationTime);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);
        }

        [TestMethod]
        public async Task GetAccess()
        {
            // create token
            var accessTokenController = TestHelper.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(maxTraffic: 100, endTime: new DateTime(2040, 1, 1), lifetime: 0, maxClient: 22);

            var clientId = Guid.NewGuid();
            var beforeUpdate = DateTime.Now;
            var clientIdentity = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = clientId, ClientIp = TestInit.TEST_ClientIp1, UserAgent = "userAgent1", ClientVersion = "1.0.0" };
            var accessParam = new AccessParams { ClientIdentity = clientIdentity, RequestEndPoint = TestInit.TEST_ServerEndPoint_G2S1 };
            
            var accessController = TestHelper.CreateAccessController();
            var access = await accessController.GetAccess(accessParam);

            Assert.IsNotNull(access.AccessId);
            Assert.AreEqual(new DateTime(2040, 1, 1), access.ExpirationTime);
            Assert.AreEqual(22, access.MaxClientCount);
            Assert.AreEqual(100, access.MaxTrafficByteCount);
            Assert.AreEqual(0, access.ReceivedTrafficByteCount);
            Assert.AreEqual(0, access.SentTrafficByteCount);
            Assert.IsNotNull(access.Secret);

            // check Client id and its properies are created 
            var clientController = TestHelper.CreateClientController();
            var client = await clientController.Get(clientIdentity.ClientId);
            Assert.AreEqual(clientIdentity.UserAgent, client.UserAgent);
            Assert.AreEqual(clientIdentity.ClientVersion, client.ClientVersion);
            
            // check connect time
            var accessUsage = await accessTokenController.GetAccessUsage(clientIdentity);
            Assert.IsTrue(accessUsage.ConnectTime >= beforeUpdate);
            Assert.IsTrue(accessUsage.ModifiedTime >= beforeUpdate);

            // check update
            beforeUpdate = DateTime.Now;
            clientIdentity = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = clientId, ClientIp = TestInit.TEST_ClientIp2, UserAgent = "userAgent2", ClientVersion = "2.0.0" };
            accessParam = new AccessParams { ClientIdentity = clientIdentity, RequestEndPoint = TestInit.TEST_ServerEndPoint_G2S1 };
            await accessController.GetAccess(accessParam);
            Assert.AreEqual(clientIdentity.UserAgent, client.UserAgent);
            Assert.AreEqual(clientIdentity.ClientVersion, client.ClientVersion);

            accessUsage = await accessTokenController.GetAccessUsage(clientIdentity);
            Assert.IsTrue(accessUsage.ConnectTime >= beforeUpdate);
            Assert.IsTrue(accessUsage.ModifiedTime >= beforeUpdate);
        }

        [TestMethod]
        public async Task GetAccess_Data_Unauthorized_EndPoint()
        {
            AccessTokenController accessTokenController = TestHelper.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(serverEndPointGroupId: TestInit.TEST_ServerEndPointGroup1);
            var tokenId = accessToken.AccessTokenId;

            // create first public token
            var accessController = TestHelper.CreateAccessController();

            //-----------
            // check: access should grant to public token 1 by another public endpoint
            //-----------
            var access = await accessController.GetAccess(new AccessParams { ClientIdentity = new ClientIdentity { ClientId = Guid.NewGuid(), TokenId = tokenId }, RequestEndPoint = TestInit.TEST_ServerEndPoint_G1S2 });
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

            //-----------
            // check: access should not grant to public token 1 by private server endpoint
            //-----------
            access = await accessController.GetAccess(new AccessParams { ClientIdentity = new ClientIdentity { ClientId = Guid.NewGuid(), TokenId = tokenId }, RequestEndPoint = TestInit.TEST_ServerEndPoint_G2S1 });
            Assert.AreEqual(AccessStatusCode.Error, access.StatusCode);

        }

        [TestMethod]
        public async Task AddUsage_Public()
        {
            // create token
            var accessTokenController = TestHelper.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(serverEndPointGroupId: TestInit.TEST_ServerEndPointGroup1, isPublic: true);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };
            var clientIdentity2 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };

            var accessController = TestHelper.CreateAccessController();
            var access1 = await accessController.GetAccess( new AccessParams { ClientIdentity = clientIdentity1, RequestEndPoint = TestInit.TEST_ServerEndPoint_G1S1 });

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

            var accessUsage = await accessTokenController.GetAccessUsage(clientIdentity1);
            Assert.AreEqual(0, accessUsage.TotalSentTraffic);
            Assert.AreEqual(0, accessUsage.TotalReceivedTraffic);

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

            accessUsage = await accessTokenController.GetAccessUsage(clientIdentity1);
            Assert.AreEqual(5, accessUsage.TotalSentTraffic);
            Assert.AreEqual(10, accessUsage.TotalReceivedTraffic);

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

            accessUsage = await accessTokenController.GetAccessUsage(clientIdentity1);
            Assert.AreEqual(10, accessUsage.TotalSentTraffic);
            Assert.AreEqual(20, accessUsage.TotalReceivedTraffic);
            
            //-----------
            // check: add usage for client 2
            //-----------
            var access2 = await accessController.GetAccess(new AccessParams { ClientIdentity = clientIdentity2, RequestEndPoint = TestInit.TEST_ServerEndPoint_G1S1 });
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

            accessUsage = await accessTokenController.GetAccessUsage(clientIdentity2);
            Assert.AreEqual(2, accessUsage.TotalSentTraffic);
            Assert.AreEqual(3, accessUsage.TotalReceivedTraffic);
            //-------------
            // check: add usage to client 1 after cycle
            //-------------

            //remove last cycle
            await PublicCycleHelper.DeleteCycle(_vhContext, PublicCycleHelper.CurrentCycleId);

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

            accessUsage = await accessTokenController.GetAccessUsage(clientIdentity2);
            Assert.AreEqual(2, accessUsage.TotalSentTraffic);
            Assert.AreEqual(3, accessUsage.TotalReceivedTraffic);

            //-------------
            // check: GetAccess should return same result
            //-------------
            var access1B = await accessController.GetAccess( new AccessParams { ClientIdentity = clientIdentity1, RequestEndPoint = TestInit.TEST_ServerEndPoint_G1S1 });
            Assert.AreEqual(JsonConvert.SerializeObject(access1), JsonConvert.SerializeObject(access1B));
        }

        [TestMethod]
        public async Task AddUsage_Private()
        {
            // create token
            var accessTokenController = TestHelper.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(serverEndPointGroupId: TestInit.TEST_ServerEndPointGroup1, isPublic: false);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };
            var clientIdentity2 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };

            var accessController = TestHelper.CreateAccessController();
            var access1 = await accessController.GetAccess(new AccessParams { ClientIdentity = clientIdentity1, RequestEndPoint = TestInit.TEST_ServerEndPoint_G1S1 });

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

            var accessUsage = await accessTokenController.GetAccessUsage(clientIdentity1);
            Assert.AreEqual(0, accessUsage.TotalSentTraffic);
            Assert.AreEqual(0, accessUsage.TotalReceivedTraffic);

            //-----------
            // check: add usage by client 1
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

            accessUsage = await accessTokenController.GetAccessUsage(clientIdentity1);
            Assert.AreEqual(5, accessUsage.TotalSentTraffic);
            Assert.AreEqual(10, accessUsage.TotalReceivedTraffic);

            // again by client 2
            access1 = await accessController.AddUsage(new UsageParams()
            {
                AccessId = access1.AccessId,
                ClientIdentity = clientIdentity2,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });

            Assert.AreEqual(10, access1.SentTrafficByteCount);
            Assert.AreEqual(20, access1.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access1.StatusCode);

            accessUsage = await accessTokenController.GetAccessUsage(clientIdentity1);
            Assert.AreEqual(10, accessUsage.TotalSentTraffic);
            Assert.AreEqual(20, accessUsage.TotalReceivedTraffic);
        }

        [TestMethod]
        public async Task GetCertificateData()
        {
            // create new ServerEndPoint
            var dnsName = "fifoo.com";
            var serverEndPointController = TestHelper.CreateServerEndPointController();
            var serverEndPointId = TestInit.TEST_ServerEndPointId_New1.ToString();
            await serverEndPointController.Create(serverEndPoint: serverEndPointId, subjectName: dnsName);

            // check serverId is null
            var serverEndPoint = await serverEndPointController.Get(serverEndPointId);
            Assert.IsNull(serverEndPoint.ServerId);

            // get certificate by accessController
            var accessController = TestHelper.CreateAccessController();
            var certBuffer = await accessController.GetSslCertificateData(serverEndPointId);
            var certificate = new X509Certificate2(certBuffer);
            Assert.AreEqual(dnsName, certificate.GetNameInfo(X509NameType.DnsName, false));

            //-----------
            // check: check serverId is set
            //-----------
            serverEndPoint = await serverEndPointController.Get(serverEndPointId);
            Assert.AreEqual(TestInit.TEST_ServerId_1, serverEndPoint.ServerId);

            //-----------
            // check: check not found
            //-----------
            try
            {
                await accessController.GetSslCertificateData(TestInit.TEST_ServerEndPointId_New2.ToString());
                Assert.Fail("KeyNotFoundException expected!");
            }
            catch (KeyNotFoundException) { }
        }

        [TestMethod]
        public async Task SendServerStatus()
        {
            var serverId1 = Guid.NewGuid();
            var accessController1 = TestHelper.CreateAccessController(serverId: serverId1);
            await accessController1.SendServerStatus(new ServerStatus { SessionCount = 10 });
            
            var serverId2 = Guid.NewGuid();
            var accessController2 = TestHelper.CreateAccessController(serverId: serverId2);
            await accessController2.SendServerStatus(new ServerStatus { SessionCount = 20 });

            var serverController = TestHelper.CreateServerController();
            var server1 = await serverController.Get(serverId1);
            Assert.AreEqual(server1.LastSessionCount, 10);

            var server2 = await serverController.Get(serverId2);
            Assert.AreEqual(server2.LastSessionCount, 20);
        }

        [TestMethod]
        public async Task AccessUsageLog_Inserted()
        {

            var accessTokenController = TestHelper.CreateAccessTokenController();
            var accessController = TestHelper.CreateAccessController();

            // create token
            var accssToken = await accessTokenController.Create();
            var clientIdentity1 = new ClientIdentity() { TokenId = accssToken.AccessTokenId, ClientIp = TestInit.TEST_ClientIp1, ClientId = Guid.NewGuid(), ClientVersion = "2.0.2.0", UserAgent = "userAgent1" };

            //-----------
            // check: add usage
            //-----------
            await accessController.AddUsage(new UsageParams() { ClientIdentity = clientIdentity1, SentTrafficByteCount = 10051, ReceivedTrafficByteCount = 20051 });
            await accessController.AddUsage(new UsageParams() { ClientIdentity = clientIdentity1, SentTrafficByteCount = 20, ReceivedTrafficByteCount = 30 });

            // query database for usage
            var accessUsageLog = (await _vhContext.AccessUsageLogs.Where(x => x.ClientId == clientIdentity1.ClientId).OrderBy(x => x.AccessUsageLogId).Take(0).ToArrayAsync()).First();
            
            Assert.AreEqual(clientIdentity1.TokenId, accessUsageLog.AccessTokenId);
            Assert.AreEqual(clientIdentity1.ClientId, accessUsageLog.ClientId);
            Assert.AreEqual(clientIdentity1.ClientIp.ToString(), accessUsageLog.ClientIp);
            Assert.AreEqual(clientIdentity1.ClientVersion, accessUsageLog.ClientVersion);
            Assert.AreEqual(20, accessUsageLog.SentTraffic);
            Assert.AreEqual(30, accessUsageLog.ReceivedTraffic);
            Assert.AreEqual(10071, accessUsageLog.CycleSentTraffic);
            Assert.AreEqual(20081, accessUsageLog.CycleReceivedTraffic);
            Assert.AreEqual(10071, accessUsageLog.TotalSentTraffic);
            Assert.AreEqual(20081, accessUsageLog.TotalReceivedTraffic);
        }
    }
}
