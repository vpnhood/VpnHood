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
    public class AccessController_Test : ControllerTest
    {
        [TestMethod]
        public async Task GetAccess_Status_Expired()
        {
            var accessTokenController = TestInit.CreateAccessTokenController();

            // create accessToken
            var accessToken = await accessTokenController.Create(TestInit.AccountId_1, endTime: new DateTime(1900, 1, 1));

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };
            var accessController = TestInit.CreateAccessController();

            var access = await accessController.AddUsage(TestInit.ServerId_1, new UsageParams()
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
            var accessTokenController = TestInit.CreateAccessTokenController();

            // create accessToken
            var accessToken = await accessTokenController.Create(TestInit.AccountId_1, maxTraffic: 14);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };
            var accessController = TestInit.CreateAccessController();

            //-----------
            // check: add usage
            //-----------
            var access = await accessController.AddUsage(TestInit.ServerId_1, new UsageParams()
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
            var accessTokenController = TestInit.CreateAccessTokenController();

            // create accessToken
            var accessToken = await accessTokenController.Create(TestInit.AccountId_1, maxTraffic: 0);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };
            var accessController = TestInit.CreateAccessController();

            //-----------
            // check: add usage
            //-----------
            var access = await accessController.AddUsage(TestInit.ServerId_1, new UsageParams()
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
            var accessTokenController = TestInit.CreateAccessTokenController();

            // create token
            var accessToken = await accessTokenController.Create(TestInit.AccountId_1, endTime: null, lifetime: 30);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };
            var accessController = TestInit.CreateAccessController();

            //-----------
            // check: add usage
            //-----------
            var access = await accessController.AddUsage(TestInit.ServerId_1, new UsageParams()
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
            var accessTokenController = TestInit.CreateAccessTokenController();

            // create token
            var accessToken = await accessTokenController.Create(TestInit.AccountId_1, endTime: null, lifetime: 30);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientIp = TestInit.ClientIp1 };
            var accessController = TestInit.CreateAccessController();
            var access = await accessController.GetAccess(TestInit.ServerId_1, new AccessParams { ClientIdentity = clientIdentity1, RequestEndPoint = TestInit.ServerEndPoint_G2S1 });
            Assert.IsNull(access.ExpirationTime);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);
        }

        [TestMethod]
        public async Task AddUsage_should_not_reset_expiration_Time()
        {
            var expectedExpirationTime = DateTime.Now.AddDays(10).Date;

            // create token
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit.AccountId_1, endTime: expectedExpirationTime, lifetime: 30);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };
            var accessController = TestInit.CreateAccessController();
            var access = await accessController.GetAccess(TestInit.ServerId_1, new AccessParams { ClientIdentity = clientIdentity1, RequestEndPoint = TestInit.ServerEndPoint_G2S1 });
            Assert.AreEqual(expectedExpirationTime, access.ExpirationTime);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);
        }

        [TestMethod]
        public async Task GetAccess()
        {
            // create token
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit.AccountId_1, maxTraffic: 100, endTime: new DateTime(2040, 1, 1), lifetime: 0, maxClient: 22);

            var clientId = Guid.NewGuid();
            var beforeUpdateTime = DateTime.Now;
            var clientIdentity = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = clientId, ClientIp = TestInit.ClientIp1, UserAgent = "userAgent1", ClientVersion = "1.0.0" };
            var accessParam = new AccessParams { ClientIdentity = clientIdentity, RequestEndPoint = TestInit.ServerEndPoint_G1S1 };
            
            var accessController = TestInit.CreateAccessController();
            var access = await accessController.GetAccess(TestInit.ServerId_1, accessParam);

            Assert.IsNotNull(access.AccessId);
            Assert.AreEqual(new DateTime(2040, 1, 1), access.ExpirationTime);
            Assert.AreEqual(22, access.MaxClientCount);
            Assert.AreEqual(100, access.MaxTrafficByteCount);
            Assert.AreEqual(0, access.ReceivedTrafficByteCount);
            Assert.AreEqual(0, access.SentTrafficByteCount);
            Assert.IsNotNull(access.Secret);

            // check Client id and its properies are created 
            var clientController = TestInit.CreateClientController();
            var client = await clientController.Get(TestInit.AccountId_1, clientId: clientId);
            Assert.AreEqual(clientIdentity.ClientId, client.ClientId);
            Assert.AreEqual(clientIdentity.UserAgent, client.UserAgent);
            Assert.AreEqual(clientIdentity.ClientVersion, client.ClientVersion);
            
            // check connect time
            var accessUsage = await accessTokenController.GetAccessUsage(TestInit.AccountId_1, clientIdentity);
            Assert.IsTrue(accessUsage.ConnectTime >= beforeUpdateTime);
            Assert.IsTrue(accessUsage.ModifiedTime >= beforeUpdateTime);

            // check update
            beforeUpdateTime = DateTime.Now;
            clientIdentity = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = clientId, ClientIp = TestInit.ClientIp2, UserAgent = "userAgent2", ClientVersion = "2.0.0" };
            accessParam = new AccessParams { ClientIdentity = clientIdentity, RequestEndPoint = TestInit.ServerEndPoint_G2S1 };
            await accessController.GetAccess(TestInit.ServerId_1, accessParam);
            Assert.AreEqual(clientIdentity.UserAgent, client.UserAgent);
            Assert.AreEqual(clientIdentity.ClientVersion, client.ClientVersion);

            accessUsage = await accessTokenController.GetAccessUsage(TestInit.AccountId_1, clientIdentity);
            Assert.IsTrue(accessUsage.ConnectTime >= beforeUpdateTime);
            Assert.IsTrue(accessUsage.ModifiedTime >= beforeUpdateTime);
        }

        [TestMethod]
        public async Task GetAccess_Data_Unauthorized_EndPoint()
        {
            AccessTokenController accessTokenController = TestInit.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit.AccountId_1, accessTokenGroupId: TestInit.AccessTokenGroupId_1);
            var tokenId = accessToken.AccessTokenId;

            // create first public token
            var accessController = TestInit.CreateAccessController();

            //-----------
            // check: access should grant to public token 1 by another public endpoint
            //-----------
            var access = await accessController.GetAccess(TestInit.ServerId_1, new AccessParams { ClientIdentity = new ClientIdentity { ClientId = Guid.NewGuid(), TokenId = tokenId }, RequestEndPoint = TestInit.ServerEndPoint_G1S2 });
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

            //-----------
            // check: access should not grant to public token 1 by private server endpoint
            //-----------
            access = await accessController.GetAccess(TestInit.ServerId_1, new AccessParams { ClientIdentity = new ClientIdentity { ClientId = Guid.NewGuid(), TokenId = tokenId }, RequestEndPoint = TestInit.ServerEndPoint_G2S1 });
            Assert.AreEqual(AccessStatusCode.Error, access.StatusCode);

        }

        [TestMethod]
        public async Task AddUsage_Public()
        {
            // create token
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit.AccountId_1, accessTokenGroupId: TestInit.AccessTokenGroupId_1, isPublic: true);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };
            var clientIdentity2 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };

            var accessController = TestInit.CreateAccessController();
            var access1 = await accessController.GetAccess(TestInit.ServerId_1, new AccessParams { ClientIdentity = clientIdentity1, RequestEndPoint = TestInit.ServerEndPoint_G1S1 });

            //--------------
            // check: zero usage
            //--------------
            access1 = await accessController.AddUsage(TestInit.ServerId_1, new UsageParams()
            {
                AccessId = access1.AccessId,
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 0,
                ReceivedTrafficByteCount = 0
            });
            Assert.AreEqual(0, access1.SentTrafficByteCount);
            Assert.AreEqual(0, access1.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access1.StatusCode);

            var accessUsage = await accessTokenController.GetAccessUsage(TestInit.AccountId_1, clientIdentity1);
            Assert.AreEqual(0, accessUsage.TotalSentTraffic);
            Assert.AreEqual(0, accessUsage.TotalReceivedTraffic);

            //-----------
            // check: add usage
            //-----------
            access1 = await accessController.AddUsage(TestInit.ServerId_1, new UsageParams()
            {
                AccessId = access1.AccessId,
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access1.SentTrafficByteCount);
            Assert.AreEqual(10, access1.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access1.StatusCode);

            accessUsage = await accessTokenController.GetAccessUsage(TestInit.AccountId_1, clientIdentity1);
            Assert.AreEqual(5, accessUsage.TotalSentTraffic);
            Assert.AreEqual(10, accessUsage.TotalReceivedTraffic);

            // again
            access1 = await accessController.AddUsage(TestInit.ServerId_1, new UsageParams()
            {
                AccessId = access1.AccessId,
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });

            Assert.AreEqual(10, access1.SentTrafficByteCount);
            Assert.AreEqual(20, access1.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access1.StatusCode);

            accessUsage = await accessTokenController.GetAccessUsage(TestInit.AccountId_1, clientIdentity1);
            Assert.AreEqual(10, accessUsage.TotalSentTraffic);
            Assert.AreEqual(20, accessUsage.TotalReceivedTraffic);
            
            //-----------
            // check: add usage for client 2
            //-----------
            var access2 = await accessController.GetAccess(TestInit.ServerId_1, new AccessParams { ClientIdentity = clientIdentity2, RequestEndPoint = TestInit.ServerEndPoint_G1S1 });
            access2 = await accessController.AddUsage(TestInit.ServerId_1, new UsageParams()
            {
                AccessId = access2.AccessId,
                ClientIdentity = clientIdentity2,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });

            Assert.AreEqual(5, access2.SentTrafficByteCount);
            Assert.AreEqual(10, access2.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access2.StatusCode);

            accessUsage = await accessTokenController.GetAccessUsage(TestInit.AccountId_1, clientIdentity2);
            Assert.AreEqual(2, accessUsage.TotalSentTraffic);
            Assert.AreEqual(3, accessUsage.TotalReceivedTraffic);
            //-------------
            // check: add usage to client 1 after cycle
            //-------------

            //remove last cycle
            using VhContext vhContext = new();
            await PublicCycleHelper.DeleteCycle(vhContext, PublicCycleHelper.CurrentCycleId);

            access1 = await accessController.AddUsage(TestInit.ServerId_1, new UsageParams()
            {
                AccessId = access1.AccessId,
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access1.SentTrafficByteCount);
            Assert.AreEqual(10, access1.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access1.StatusCode);

            accessUsage = await accessTokenController.GetAccessUsage(TestInit.AccountId_1, clientIdentity2);
            Assert.AreEqual(2, accessUsage.TotalSentTraffic);
            Assert.AreEqual(3, accessUsage.TotalReceivedTraffic);

            //-------------
            // check: GetAccess should return same result
            //-------------
            var access1B = await accessController.GetAccess(TestInit.ServerId_1, new AccessParams { ClientIdentity = clientIdentity1, RequestEndPoint = TestInit.ServerEndPoint_G1S1 });
            Assert.AreEqual(JsonConvert.SerializeObject(access1), JsonConvert.SerializeObject(access1B));
        }

        [TestMethod]
        public async Task AddUsage_Private()
        {
            // create token
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit.AccountId_1, accessTokenGroupId: TestInit.AccessTokenGroupId_1, isPublic: false);

            var clientIdentity1 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };
            var clientIdentity2 = new ClientIdentity() { TokenId = accessToken.AccessTokenId, ClientId = Guid.NewGuid() };

            var accessController = TestInit.CreateAccessController();
            var access1 = await accessController.GetAccess(TestInit.ServerId_1, new AccessParams { ClientIdentity = clientIdentity1, RequestEndPoint = TestInit.ServerEndPoint_G1S1 });

            //--------------
            // check: zero usage
            //--------------
            access1 = await accessController.AddUsage(TestInit.ServerId_1, new UsageParams()
            {
                AccessId = access1.AccessId,
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 0,
                ReceivedTrafficByteCount = 0
            });
            Assert.AreEqual(0, access1.SentTrafficByteCount);
            Assert.AreEqual(0, access1.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access1.StatusCode);

            var accessUsage = await accessTokenController.GetAccessUsage(TestInit.AccountId_1, clientIdentity1);
            Assert.AreEqual(0, accessUsage.TotalSentTraffic);
            Assert.AreEqual(0, accessUsage.TotalReceivedTraffic);

            //-----------
            // check: add usage by client 1
            //-----------
            access1 = await accessController.AddUsage(TestInit.ServerId_1, new UsageParams()
            {
                AccessId = access1.AccessId,
                ClientIdentity = clientIdentity1,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access1.SentTrafficByteCount);
            Assert.AreEqual(10, access1.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access1.StatusCode);

            accessUsage = await accessTokenController.GetAccessUsage(TestInit.AccountId_1, clientIdentity1);
            Assert.AreEqual(5, accessUsage.TotalSentTraffic);
            Assert.AreEqual(10, accessUsage.TotalReceivedTraffic);

            // again by client 2
            access1 = await accessController.AddUsage(TestInit.ServerId_1, new UsageParams()
            {
                AccessId = access1.AccessId,
                ClientIdentity = clientIdentity2,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });

            Assert.AreEqual(10, access1.SentTrafficByteCount);
            Assert.AreEqual(20, access1.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access1.StatusCode);

            accessUsage = await accessTokenController.GetAccessUsage(TestInit.AccountId_1, clientIdentity1);
            Assert.AreEqual(10, accessUsage.TotalSentTraffic);
            Assert.AreEqual(20, accessUsage.TotalReceivedTraffic);
        }

        [TestMethod]
        public async Task GetCertificateData()
        {
            // create new ServerEndPoint
            var dnsName = $"CN=fifoo-{Guid.NewGuid():N}.com";
            var serverEndPointController = TestInit.CreateServerEndPointController();
            var publicEndPointId = TestInit.ServerEndPoint_New1.ToString();
            await serverEndPointController.Create(TestInit.AccountId_1, publicEndPoint: publicEndPointId, subjectName: dnsName);

            // check serverId is null
            var serverEndPoint = await serverEndPointController.Get(TestInit.AccountId_1, publicEndPointId);
            Assert.IsNull(serverEndPoint.ServerId);

            // get certificate by accessController
            var accessController = TestInit.CreateAccessController();
            var certBuffer = await accessController.GetSslCertificateData(TestInit.ServerId_1, publicEndPointId);
            var certificate = new X509Certificate2(certBuffer);
            Assert.AreEqual(dnsName, certificate.Subject);

            //-----------
            // check: check serverId is set after GetSslCertificateData
            //-----------
            serverEndPoint = await serverEndPointController.Get(TestInit.AccountId_1, publicEndPointId);
            Assert.AreEqual(TestInit.ServerId_1, serverEndPoint.ServerId);

            //-----------
            // check: check not found
            //-----------
            try
            {
                await accessController.GetSslCertificateData(TestInit.ServerId_1, TestInit.ServerEndPoint_New2.ToString());
                Assert.Fail("NotExistsException expected!");
            }
            catch (Exception ex) when (AccessUtil.IsNotExistsException(ex)) { }
        }

        [TestMethod]
        public async Task SendServerStatus()
        {
            var serverId1 = TestInit.ServerId_1;
            var accessController1 = TestInit.CreateAccessController(serverId: serverId1);
            await accessController1.SendServerStatus(serverId1, new ServerStatus { SessionCount = 10 });
            
            var serverId2 = TestInit.ServerId_2;
            var accessController2 = TestInit.CreateAccessController(serverId: serverId2);
            await accessController2.SendServerStatus(serverId2, new ServerStatus { SessionCount = 20 });

            var serverController = TestInit.CreateServerController();

            var serverData1 = await serverController.Get(TestInit.AccountId_1, serverId1);
            Assert.AreEqual(serverData1.Status.SessionCount, 10);

            var serverData2 = await serverController.Get(TestInit.AccountId_1, serverId2);
            Assert.AreEqual(serverData2.Status.SessionCount, 20); 
        }

        [TestMethod]
        public async Task AccessUsageLog_Inserted()
        {

            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessController = TestInit.CreateAccessController();

            // create token
            var accssToken = await accessTokenController.Create(TestInit.AccountId_1);
            var clientIdentity1 = new ClientIdentity() { TokenId = accssToken.AccessTokenId, ClientIp = TestInit.ClientIp1, ClientId = Guid.NewGuid(), ClientVersion = "2.0.2.0", UserAgent = "userAgent1" };

            //-----------
            // check: add usage
            //-----------
            await accessController.AddUsage(TestInit.ServerId_1, new UsageParams() { ClientIdentity = clientIdentity1, SentTrafficByteCount = 10051, ReceivedTrafficByteCount = 20051 });
            await accessController.AddUsage(TestInit.ServerId_1, new UsageParams() { ClientIdentity = clientIdentity1, SentTrafficByteCount = 20, ReceivedTrafficByteCount = 30 });

            // query database for usage
            var accessUsageLogs = await accessTokenController.GetAccessUsageLogs(TestInit.AccountId_1, accessTokenId: accssToken.AccessTokenId, clientId: clientIdentity1.ClientId, recordCount: 100);
            var accessUsageLog = accessUsageLogs[0];
            
            Assert.AreEqual(clientIdentity1.TokenId, accessUsageLog.AccessUsage.AccessTokenId);
            Assert.AreEqual(clientIdentity1.ClientId, accessUsageLog.ClientKeyId);
            Assert.AreEqual(clientIdentity1.ClientIp.ToString(), accessUsageLog.ClientIp);
            Assert.AreEqual(clientIdentity1.ClientVersion, accessUsageLog.ClientVersion);
            Assert.AreEqual(20, accessUsageLog.SentTraffic);
            Assert.AreEqual(30, accessUsageLog.ReceivedTraffic);
            Assert.AreEqual(10071, accessUsageLog.CycleSentTraffic);
            Assert.AreEqual(20081, accessUsageLog.CycleReceivedTraffic);
            Assert.AreEqual(10071, accessUsageLog.TotalSentTraffic);
            Assert.AreEqual(20081, accessUsageLog.TotalReceivedTraffic);
            Assert.AreEqual(TestInit.ServerId_1, accessUsageLog.ServerId);
        }
    }
}
