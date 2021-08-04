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
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, endTime: new DateTime(1900, 1, 1));

            var clientInfo1 = new ClientInfo() { ClientId = Guid.NewGuid() };
            var accessController = TestInit1.CreateAccessController();

            var access = await accessController.Get(TestInit1.ServerId_1, new AccessRequest() { TokenId = accessToken.AccessTokenId, ClientInfo = clientInfo1, RequestEndPoint = TestInit1.ServerEndPoint_G1S1 });
            access = await accessController.AddUsage(TestInit1.ServerId_1, new UsageParams()
            {
                AccessId = access.AccessId,
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
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, maxTraffic: 14);

            // get access
            var clientInfo1 = new ClientInfo() { ClientId = Guid.NewGuid() };
            var accessController = TestInit1.CreateAccessController();
            var access = await accessController.Get(TestInit1.ServerId_1, new()
            {
                TokenId = accessToken.AccessTokenId,
                ClientInfo = clientInfo1,
                RequestEndPoint = TestInit1.ServerEndPoint_G1S1
            }); ;

            //-----------
            // check: add usage
            //-----------
            access = await accessController.AddUsage(TestInit1.ServerId_1, new UsageParams()
            {
                AccessId = access.AccessId,
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
            var clientInfo = new ClientInfo() { ClientId = Guid.NewGuid() };
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, maxTraffic: 0);
            var accessController = TestInit1.CreateAccessController();

            //-----------
            // check: add usage
            //-----------
            var access = await accessController.Get(TestInit1.ServerId_1, new AccessRequest { TokenId = accessToken.AccessTokenId, ClientInfo = clientInfo, RequestEndPoint = TestInit1.ServerEndPoint_G1S1 });
            access = await accessController.AddUsage(TestInit1.ServerId_1, new UsageParams()
            {
                AccessId = access.AccessId,
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
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, endTime: null, lifetime: 30);

            var clientInfo1 = new ClientInfo() { ClientId = Guid.NewGuid() };
            var accessController = TestInit1.CreateAccessController();

            //-----------
            // check: add usage
            //-----------
            var access = await accessController.Get(TestInit1.ServerId_1, new AccessRequest()
            {
                TokenId = accessToken.AccessTokenId,
                ClientInfo = clientInfo1,
                RequestEndPoint = TestInit1.ServerEndPoint_G1S1
            });
            Assert.AreEqual(0, access.SentTrafficByteCount);
            Assert.AreEqual(0, access.ReceivedTrafficByteCount);
            Assert.IsTrue((access.ExpirationTime.Value - DateTime.Now.AddDays(30)).TotalSeconds < 10);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);
        }

        [TestMethod]
        public async Task GetAccess_should_not_reset_expiration_Time()
        {
            var expectedExpirationTime = DateTime.Now.AddDays(10).Date;

            // create token
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, accessTokenGroupId: TestInit1.AccessTokenGroupId_2, endTime: expectedExpirationTime, lifetime: 30);

            var clientInfo1 = new ClientInfo() { ClientId = Guid.NewGuid() };
            var accessController = TestInit1.CreateAccessController();
            var access = await accessController.Get(TestInit1.ServerId_1, new AccessRequest { TokenId = accessToken.AccessTokenId, ClientInfo = clientInfo1, RequestEndPoint = TestInit1.ServerEndPoint_G2S1 });
            Assert.AreEqual(expectedExpirationTime, access.ExpirationTime);
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);
        }

        [TestMethod]
        public async Task GetAccess()
        {
            // create token
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, maxTraffic: 100, endTime: new DateTime(2040, 1, 1), lifetime: 0, maxClient: 22);

            var clientId = Guid.NewGuid();
            var beforeUpdateTime = DateTime.Now;
            var clientInfo = new ClientInfo() { ClientId = clientId, ClientIp = TestInit1.ClientIp1, UserAgent = "userAgent1", ClientVersion = "1.0.0" };
            var accessRequest = new AccessRequest { TokenId = accessToken.AccessTokenId, ClientInfo = clientInfo, RequestEndPoint = TestInit1.ServerEndPoint_G1S1 };

            var accessController = TestInit1.CreateAccessController();
            var access = await accessController.Get(TestInit1.ServerId_1, accessRequest);

            Assert.IsNotNull(access.AccessId);
            Assert.AreEqual(new DateTime(2040, 1, 1), access.ExpirationTime);
            Assert.AreEqual(22, access.MaxClientCount);
            Assert.AreEqual(100, access.MaxTrafficByteCount);
            Assert.AreEqual(0, access.ReceivedTrafficByteCount);
            Assert.AreEqual(0, access.SentTrafficByteCount);
            Assert.IsNotNull(access.Secret);

            // check Client id and its properies are created 
            var clientController = TestInit.CreateClientController();
            var client = await clientController.Get(TestInit1.ProjectId, clientId: clientId);
            Assert.AreEqual(clientInfo.ClientId, client.ClientId);
            Assert.AreEqual(clientInfo.UserAgent, client.UserAgent);
            Assert.AreEqual(clientInfo.ClientVersion, client.ClientVersion);

            // check connect time
            var accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, accessRequest.TokenId, clientInfo.ClientId);
            Assert.IsTrue(accessUsage.ConnectTime >= beforeUpdateTime);
            Assert.IsTrue(accessUsage.ModifiedTime >= beforeUpdateTime);

            // check update
            beforeUpdateTime = DateTime.Now;
            clientInfo = new ClientInfo() { ClientId = clientId, ClientIp = TestInit1.ClientIp2, UserAgent = "userAgent2", ClientVersion = "2.0.0" };
            accessRequest = new AccessRequest { TokenId = accessToken.AccessTokenId, ClientInfo = clientInfo, RequestEndPoint = TestInit1.ServerEndPoint_G1S1 };
            await accessController.Get(TestInit1.ServerId_1, accessRequest);
            client = await clientController.Get(TestInit1.ProjectId, clientId: clientId);
            Assert.AreEqual(clientInfo.UserAgent, client.UserAgent);
            Assert.AreEqual(clientInfo.ClientVersion, client.ClientVersion);

            accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, accessRequest.TokenId, clientInfo.ClientId);
            Assert.IsTrue(accessUsage.ConnectTime >= beforeUpdateTime);
            Assert.IsTrue(accessUsage.ModifiedTime >= beforeUpdateTime);
        }

        [TestMethod]
        public async Task GetAccess_Data_Unauthorized_EndPoint()
        {
            AccessTokenController accessTokenController = TestInit.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, accessTokenGroupId: TestInit1.AccessTokenGroupId_1);
            var tokenId = accessToken.AccessTokenId;

            // create first public token
            var accessController = TestInit1.CreateAccessController();

            //-----------
            // check: access should grant to public token 1 by another public endpoint
            //-----------
            var access = await accessController.Get(TestInit1.ServerId_1, TestInit1.CreateAccessRequest(tokenId, null, TestInit1.ServerEndPoint_G1S2));
            Assert.AreEqual(AccessStatusCode.Ok, access.StatusCode);

            //-----------
            // check: access should not grant to public token 1 by private server endpoint
            //-----------
            try
            {
                access = await accessController.Get(TestInit1.ServerId_1, TestInit1.CreateAccessRequest(tokenId, null, TestInit1.ServerEndPoint_G2S1)) ;
                Assert.Fail("Exception expected");

            }
            catch (Exception ex) when (AccessUtil.IsNotExistsException(ex)) { }
        }

        [TestMethod]
        public async Task AddUsage_Public()
        {
            // create token
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, accessTokenGroupId: TestInit1.AccessTokenGroupId_1, isPublic: true);

            var accessController = TestInit1.CreateAccessController();
            var accessRequet1 = TestInit1.CreateAccessRequest(accessToken.AccessTokenId);
            var access1 = await accessController.Get(TestInit1.ServerId_1, accessRequet1);

            //--------------
            // check: zero usage
            //--------------
            access1 = await accessController.AddUsage(TestInit1.ServerId_1, new UsageParams()
            {
                AccessId = access1.AccessId,
                SentTrafficByteCount = 0,
                ReceivedTrafficByteCount = 0
            });
            Assert.AreEqual(0, access1.SentTrafficByteCount);
            Assert.AreEqual(0, access1.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access1.StatusCode);

            var accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, accessRequet1.TokenId, accessRequet1.ClientInfo.ClientId);
            Assert.AreEqual(0, accessUsage.TotalSentTraffic);
            Assert.AreEqual(0, accessUsage.TotalReceivedTraffic);

            //-----------
            // check: add usage
            //-----------
            access1 = await accessController.AddUsage(TestInit1.ServerId_1, new UsageParams()
            {
                AccessId = access1.AccessId,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access1.SentTrafficByteCount);
            Assert.AreEqual(10, access1.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access1.StatusCode);

            accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, accessRequet1.TokenId, accessRequet1.ClientInfo.ClientId);
            Assert.AreEqual(5, accessUsage.TotalSentTraffic);
            Assert.AreEqual(10, accessUsage.TotalReceivedTraffic);

            // again
            access1 = await accessController.AddUsage(TestInit1.ServerId_1, new UsageParams()
            {
                AccessId = access1.AccessId,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });

            Assert.AreEqual(10, access1.SentTrafficByteCount);
            Assert.AreEqual(20, access1.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access1.StatusCode);

            accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, accessRequet1.TokenId, accessRequet1.ClientInfo.ClientId);
            Assert.AreEqual(10, accessUsage.TotalSentTraffic);
            Assert.AreEqual(20, accessUsage.TotalReceivedTraffic);

            //-----------
            // check: add usage for client 2
            //-----------
            var accessRequet2 = TestInit1.CreateAccessRequest(accessToken.AccessTokenId);
            var access2 = await accessController.Get(TestInit1.ServerId_1, accessRequet2);
            access2 = await accessController.AddUsage(TestInit1.ServerId_1, new UsageParams()
            {
                AccessId = access2.AccessId,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });

            Assert.AreEqual(5, access2.SentTrafficByteCount);
            Assert.AreEqual(10, access2.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access2.StatusCode);

            accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, accessRequet2.TokenId, accessRequet2.ClientInfo.ClientId);
            Assert.AreEqual(5, accessUsage.TotalSentTraffic);
            Assert.AreEqual(10, accessUsage.TotalReceivedTraffic);
            //-------------
            // check: add usage to client 1 after cycle
            //-------------

            //remove last cycle
            using VhContext vhContext = new();
            await PublicCycleHelper.DeleteCycle(PublicCycleHelper.CurrentCycleId);
            await PublicCycleHelper.UpdateCycle();

            access1 = await accessController.AddUsage(TestInit1.ServerId_1, new UsageParams()
            {
                AccessId = access1.AccessId,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access1.SentTrafficByteCount);
            Assert.AreEqual(10, access1.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access1.StatusCode);

            //-------------
            // check: GetAccess should return same result
            //-------------
            var access1B = await accessController.Get(TestInit1.ServerId_1, accessRequet1);
            Assert.AreEqual(5, access1B.SentTrafficByteCount);
            Assert.AreEqual(10, access1B.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access1B.StatusCode);

            access2 = await accessController.AddUsage(TestInit1.ServerId_1, new UsageParams()
            {
                AccessId = access2.AccessId,
                SentTrafficByteCount = 50,
                ReceivedTrafficByteCount = 100
            });
            accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, accessRequet2.TokenId, accessRequet2.ClientInfo.ClientId);
            Assert.AreEqual(50, accessUsage.CycleSentTraffic);
            Assert.AreEqual(100, accessUsage.CycleReceivedTraffic);

        }

        [TestMethod]
        public async Task AddUsage_Private()
        {
            // create token
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, accessTokenGroupId: TestInit1.AccessTokenGroupId_1, isPublic: false);

            var accessController = TestInit1.CreateAccessController();
            var accessRequest1 = TestInit1.CreateAccessRequest(accessToken.AccessTokenId);
            var access1 = await accessController.Get(TestInit1.ServerId_1, accessRequest1);

            //--------------
            // check: zero usage
            //--------------
            access1 = await accessController.AddUsage(TestInit1.ServerId_1, new UsageParams()
            {
                AccessId = access1.AccessId,
                SentTrafficByteCount = 0,
                ReceivedTrafficByteCount = 0
            });
            Assert.AreEqual(0, access1.SentTrafficByteCount);
            Assert.AreEqual(0, access1.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access1.StatusCode);

            var accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, accessRequest1.TokenId, accessRequest1.ClientInfo.ClientId);
            Assert.AreEqual(0, accessUsage.TotalSentTraffic);
            Assert.AreEqual(0, accessUsage.TotalReceivedTraffic);

            //-----------
            // check: add usage by client 1
            //-----------
            access1 = await accessController.AddUsage(TestInit1.ServerId_1, new UsageParams()
            {
                AccessId = access1.AccessId,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });
            Assert.AreEqual(5, access1.SentTrafficByteCount);
            Assert.AreEqual(10, access1.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access1.StatusCode);

            accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, accessRequest1.TokenId, accessRequest1.ClientInfo.ClientId);
            Assert.AreEqual(5, accessUsage.TotalSentTraffic);
            Assert.AreEqual(10, accessUsage.TotalReceivedTraffic);

            // again by client 2
            var accessRequest2 = TestInit1.CreateAccessRequest(accessToken.AccessTokenId);
            var access2 = await accessController.Get(TestInit1.ServerId_1, accessRequest2);
            access2 = await accessController.AddUsage(TestInit1.ServerId_1, new UsageParams()
            {
                AccessId = access2.AccessId,
                SentTrafficByteCount = 5,
                ReceivedTrafficByteCount = 10
            });

            Assert.AreEqual(10, access2.SentTrafficByteCount);
            Assert.AreEqual(20, access2.ReceivedTrafficByteCount);
            Assert.AreEqual(AccessStatusCode.Ok, access2.StatusCode);

            accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, accessRequest2.TokenId, accessRequest2.ClientInfo.ClientId);
            Assert.AreEqual(10, accessUsage.TotalSentTraffic);
            Assert.AreEqual(20, accessUsage.TotalReceivedTraffic);
        }

        [TestMethod]
        public async Task GetCertificateData()
        {
            // create new ServerEndPoint
            var dnsName = $"CN=fifoo-{Guid.NewGuid():N}.com";
            var serverEndPointController = TestInit.CreateServerEndPointController();
            var publicEndPointId = TestInit1.ServerEndPoint_New1.ToString();
            await serverEndPointController.Create(TestInit1.ProjectId, publicEndPoint: publicEndPointId, subjectName: dnsName);

            // check serverId is null
            var serverEndPoint = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPointId);
            Assert.IsNull(serverEndPoint.ServerId);

            // get certificate by accessController
            var accessController = TestInit1.CreateAccessController();
            var certBuffer = await accessController.GetSslCertificateData(TestInit1.ServerId_1, publicEndPointId);
            var certificate = new X509Certificate2(certBuffer);
            Assert.AreEqual(dnsName, certificate.Subject);

            //-----------
            // check: check serverId is set after GetSslCertificateData
            //-----------
            serverEndPoint = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPointId);
            Assert.AreEqual(TestInit1.ServerId_1, serverEndPoint.ServerId);

            //-----------
            // check: check not found
            //-----------
            try
            {
                await accessController.GetSslCertificateData(TestInit1.ServerId_1, TestInit1.ServerEndPoint_New2.ToString());
                Assert.Fail("NotExistsException expected!");
            }
            catch (Exception ex) when (AccessUtil.IsNotExistsException(ex)) { }
        }

        [TestMethod]
        public async Task SendServerStatus()
        {
            var serverId1 = TestInit1.ServerId_1;
            var accessController1 = TestInit1.CreateAccessController();
            await accessController1.SendServerStatus(serverId1, new ServerStatus { SessionCount = 10 });

            var serverId2 = TestInit1.ServerId_2;
            var accessController2 = TestInit1.CreateAccessController();
            await accessController2.SendServerStatus(serverId2, new ServerStatus { SessionCount = 20 });

            var serverController = TestInit.CreateServerController();

            var serverData1 = await serverController.Get(TestInit1.ProjectId, serverId1);
            Assert.AreEqual(serverData1.Status.SessionCount, 10);

            var serverData2 = await serverController.Get(TestInit1.ProjectId, serverId2);
            Assert.AreEqual(serverData2.Status.SessionCount, 20);
        }

        [TestMethod]
        public async Task AccessUsageLog_Inserted()
        {

            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessController = TestInit1.CreateAccessController();

            // create token
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId);
            var accessRequest = TestInit1.CreateAccessRequest(accessToken.AccessTokenId);
            accessRequest.ClientInfo = new ClientInfo() { ClientIp = TestInit1.ClientIp1, ClientId = Guid.NewGuid(), ClientVersion = "2.0.2.0", UserAgent = "userAgent1" };
            var access = await accessController.Get(TestInit1.ServerId_1, accessRequest);

            //-----------
            // check: add usage
            //-----------
            await accessController.AddUsage(TestInit1.ServerId_1, new UsageParams() { AccessId = access.AccessId, SentTrafficByteCount = 10051, ReceivedTrafficByteCount = 20051 });
            await accessController.AddUsage(TestInit1.ServerId_1, new UsageParams() { AccessId = access.AccessId, SentTrafficByteCount = 20, ReceivedTrafficByteCount = 30 });

            // query database for usage
            var accessUsageLogs = await accessTokenController.GetAccessUsageLogs(TestInit1.ProjectId, accessTokenId: accessToken.AccessTokenId, clientId: accessRequest.ClientInfo.ClientId, recordCount: 100);
            var accessUsageLog = accessUsageLogs[0];

            Assert.AreEqual(accessToken.AccessTokenId, accessUsageLog.AccessUsage.AccessTokenId);
            Assert.AreEqual(accessRequest.ClientInfo.ClientId, accessUsageLog.Client.ClientId);
            Assert.AreEqual(accessRequest.ClientInfo.ClientIp.ToString(), accessUsageLog.ClientIp);
            Assert.AreEqual(accessRequest.ClientInfo.ClientVersion, accessUsageLog.ClientVersion);
            Assert.AreEqual(20, accessUsageLog.SentTraffic);
            Assert.AreEqual(30, accessUsageLog.ReceivedTraffic);
            Assert.AreEqual(10071, accessUsageLog.CycleSentTraffic);
            Assert.AreEqual(20081, accessUsageLog.CycleReceivedTraffic);
            Assert.AreEqual(10071, accessUsageLog.TotalSentTraffic);
            Assert.AreEqual(20081, accessUsageLog.TotalReceivedTraffic);
            Assert.AreEqual(TestInit1.ServerId_1, accessUsageLog.ServerId);
        }
    }
}
