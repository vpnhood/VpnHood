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
using VpnHood.Common;
using VpnHood.Common.Messaging;
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
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, createParams: new() { EndTime = new DateTime(1900, 1, 1) });
            var accessController = TestInit1.CreateAccessController();

            var sessionResponseEx = await accessController.Session_Create(TestInit1.ServerId_1, TestInit1.CreateSessionRequestEx(accessToken: accessToken));
            var sessionResponse = await accessController.Session_AddUsage(TestInit1.ServerId_1, sessionResponseEx.SessionId, closeSession: false, usageInfo: new UsageInfo()
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });
            Assert.AreEqual(5, sessionResponse.AccessUsage?.SentTraffic);
            Assert.AreEqual(10, sessionResponse.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.AccessExpired, sessionResponse.ErrorCode);
        }

        [TestMethod]
        public async Task GetAccess_Status_TrafficOverflow()
        {
            var accessTokenController = TestInit.CreateAccessTokenController();

            // create accessToken
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, createParams: new() { MaxTraffic = 14 });

            // get access
            var accessController = TestInit1.CreateAccessController();
            var sessionResponseEx = await accessController.Session_Create(TestInit1.ServerId_1, TestInit1.CreateSessionRequestEx(accessToken));

            //-----------
            // check: add usage
            //-----------
            var sessionResponse = await accessController.Session_AddUsage(TestInit1.ServerId_1, sessionResponseEx.SessionId, closeSession: false, usageInfo: new UsageInfo()
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });
            Assert.AreEqual(5, sessionResponse.AccessUsage?.SentTraffic);
            Assert.AreEqual(10, sessionResponse.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.AccessTrafficOverflow, sessionResponse.ErrorCode);
        }

        [TestMethod]
        public async Task GetAccess_Status_No_TrafficOverflow_when_maxTraffic_is_zero()
        {
            var accessTokenController = TestInit.CreateAccessTokenController();

            // create accessToken
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, createParams: new() { MaxTraffic = 0 });
            var accessController = TestInit1.CreateAccessController();

            //-----------
            // check: add usage
            //-----------
            var sessionResponseEx = await accessController.Session_Create(TestInit1.ServerId_1, TestInit1.CreateSessionRequestEx(accessToken));
            var sessionResponse = await accessController.Session_AddUsage(TestInit1.ServerId_1, sessionResponseEx.SessionId, new UsageInfo()
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });
            Assert.AreEqual(5, sessionResponse.AccessUsage?.SentTraffic);
            Assert.AreEqual(10, sessionResponse.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, sessionResponse.ErrorCode);
        }

        [TestMethod]
        public async Task AddAccessUsage_set_expirationtime_first_use()
        {
            var accessTokenController = TestInit.CreateAccessTokenController();

            // create token
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, createParams: new() { EndTime = null, Lifetime = 30 });
            var accessController = TestInit1.CreateAccessController();

            //-----------
            // check: add usage
            //-----------
            var sessionResponseEx = await accessController.Session_Create(TestInit1.ServerId_1, TestInit1.CreateSessionRequestEx(accessToken));
            Assert.IsNotNull(sessionResponseEx.AccessUsage);
            Assert.AreEqual(0, sessionResponseEx.AccessUsage.SentTraffic);
            Assert.AreEqual(0, sessionResponseEx.AccessUsage.ReceivedTraffic);
            Assert.IsNotNull(sessionResponseEx.AccessUsage.ExpirationTime);
            Assert.IsTrue((sessionResponseEx.AccessUsage.ExpirationTime.Value - DateTime.Now.AddDays(30)).TotalSeconds < 10);
            Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode);
        }

        [TestMethod]
        public async Task GetAccess_should_not_reset_expiration_Time()
        {
            var expectedExpirationTime = DateTime.Now.AddDays(10).Date;

            // create token
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, new() { AccessTokenGroupId = TestInit1.AccessTokenGroupId_2, EndTime = expectedExpirationTime, Lifetime = 30 });

            var accessController = TestInit1.CreateAccessController();
            var sessionResponseEx = await accessController.Session_Create(TestInit1.ServerId_1, TestInit1.CreateSessionRequestEx(accessToken, hostEndPoint: TestInit1.HostEndPoint_G2S1));
            Assert.AreEqual(expectedExpirationTime, sessionResponseEx.AccessUsage?.ExpirationTime);
            Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode);
        }

        [TestMethod]
        public async Task GetAccess()
        {
            // create token
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, createParams: new() { MaxTraffic = 100, EndTime = new DateTime(2040, 1, 1), Lifetime = 0, MaxClient = 22 });

            var beforeUpdateTime = DateTime.Now;
            var sessionRequestEx = TestInit1.CreateSessionRequestEx(accessToken, hostEndPoint: TestInit1.HostEndPoint_G1S1, clientIp: TestInit1.ClientIp1);
            sessionRequestEx.ClientInfo.UserAgent = "userAgent1";
            sessionRequestEx.ClientInfo.ClientVersion = "1.0.0";
            var clientInfo = sessionRequestEx.ClientInfo;

            var accessController = TestInit1.CreateAccessController();
            var sessionResponseEx = await accessController.Session_Create(TestInit1.ServerId_1, sessionRequestEx);

            Assert.IsNotNull(sessionResponseEx.AccessUsage);
            Assert.IsTrue(sessionResponseEx.SessionId > 0);
            Assert.AreEqual(new DateTime(2040, 1, 1), sessionResponseEx.AccessUsage.ExpirationTime);
            Assert.AreEqual(22, sessionResponseEx.AccessUsage.MaxClientCount);
            Assert.AreEqual(100, sessionResponseEx.AccessUsage.MaxTraffic);
            Assert.AreEqual(0, sessionResponseEx.AccessUsage.ReceivedTraffic);
            Assert.AreEqual(0, sessionResponseEx.AccessUsage.SentTraffic);
            Assert.IsNotNull(sessionResponseEx.SessionKey);

            // check Client id and its properies are created 
            var clientController = TestInit.CreateClientController();
            var client = await clientController.Get(TestInit1.ProjectId, clientId: clientInfo.ClientId);
            Assert.AreEqual(clientInfo.ClientId, client.UserClientId);
            Assert.AreEqual(clientInfo.UserAgent, client.UserAgent);
            Assert.AreEqual(clientInfo.ClientVersion, client.ClientVersion);

            // check created time
            var accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, sessionRequestEx.TokenId, clientInfo.ClientId);
            Assert.IsTrue(accessUsage.CreatedTime >= beforeUpdateTime);
            Assert.IsTrue(accessUsage.ModifiedTime >= beforeUpdateTime);

            // check updating same client
            beforeUpdateTime = DateTime.Now;
            sessionRequestEx.ClientIp = TestInit1.ClientIp2;
            sessionRequestEx.ClientInfo.UserAgent = "userAgent2";
            sessionRequestEx.ClientInfo.ClientVersion = "2.0.0";
            await accessController.Session_Create(TestInit1.ServerId_1, sessionRequestEx);
            client = await clientController.Get(TestInit1.ProjectId, clientId: sessionRequestEx.ClientInfo.ClientId);
            Assert.AreEqual(clientInfo.UserAgent, client.UserAgent);
            Assert.AreEqual(clientInfo.ClientVersion, client.ClientVersion);

            accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, sessionRequestEx.TokenId, clientInfo.ClientId);
            Assert.IsTrue(accessUsage.ModifiedTime >= beforeUpdateTime);
        }

        [TestMethod]
        public async Task GetAccess_Data_Unauthorized_EndPoint()
        {
            AccessTokenController accessTokenController = TestInit.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, new() { AccessTokenGroupId = TestInit1.AccessTokenGroupId_1 });

            // create first public token
            var accessController = TestInit1.CreateAccessController();

            //-----------
            // check: access should grant to public token 1 by another public endpoint
            //-----------
            var sessionRequestEx = await accessController.Session_Create(TestInit1.ServerId_1, TestInit1.CreateSessionRequestEx(accessToken, hostEndPoint: TestInit1.HostEndPoint_G1S2));
            Assert.AreEqual(SessionErrorCode.Ok, sessionRequestEx.ErrorCode);

            //-----------
            // check: access should not grant to public token 1 by private server endpoint
            //-----------
            try
            {
                sessionRequestEx = await accessController.Session_Create(TestInit1.ServerId_1, TestInit1.CreateSessionRequestEx(accessToken, hostEndPoint: TestInit1.HostEndPoint_G2S1));
                Assert.Fail("Exception expected");

            }
            catch (Exception ex) when (AccessUtil.IsNotExistsException(ex)) { }
        }

        [TestMethod]
        public async Task AddUsage_Public()
        {
            // create token
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, createParams: new() { AccessTokenGroupId = TestInit1.AccessTokenGroupId_1, IsPublic = true });

            var accessController = TestInit1.CreateAccessController();
            var sessionRequestEx1 = TestInit1.CreateSessionRequestEx(accessToken);
            var sessionResponseEx1 = await accessController.Session_Create(TestInit1.ServerId_1, sessionRequestEx1);

            //--------------
            // check: zero usage
            //--------------
            var baseResponse = await accessController.Session_AddUsage(TestInit1.ServerId_1, sessionResponseEx1.SessionId, new UsageInfo()
            {
                SentTraffic = 0,
                ReceivedTraffic = 0
            });
            Assert.AreEqual(0, baseResponse.AccessUsage?.SentTraffic);
            Assert.AreEqual(0, baseResponse.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, baseResponse.ErrorCode);

            var accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, sessionRequestEx1.TokenId, sessionRequestEx1.ClientInfo.ClientId);
            Assert.AreEqual(0, accessUsage.TotalSentTraffic);
            Assert.AreEqual(0, accessUsage.TotalReceivedTraffic);

            //-----------
            // check: add usage
            //-----------
            baseResponse = await accessController.Session_AddUsage(TestInit1.ServerId_1, sessionResponseEx1.SessionId, new UsageInfo()
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });
            Assert.AreEqual(5, baseResponse.AccessUsage?.SentTraffic);
            Assert.AreEqual(10, baseResponse.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, baseResponse.ErrorCode);

            accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, sessionRequestEx1.TokenId, sessionRequestEx1.ClientInfo.ClientId);
            Assert.AreEqual(5, accessUsage.TotalSentTraffic);
            Assert.AreEqual(10, accessUsage.TotalReceivedTraffic);

            // again
            baseResponse = await accessController.Session_AddUsage(TestInit1.ServerId_1, sessionResponseEx1.SessionId, new UsageInfo()
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });

            Assert.AreEqual(10, baseResponse.AccessUsage?.SentTraffic);
            Assert.AreEqual(20, baseResponse.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, baseResponse.ErrorCode);

            accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, sessionRequestEx1.TokenId, sessionRequestEx1.ClientInfo.ClientId);
            Assert.AreEqual(10, accessUsage.TotalSentTraffic);
            Assert.AreEqual(20, accessUsage.TotalReceivedTraffic);

            //-----------
            // check: add usage for client 2
            //-----------
            var sessionRequestEx2 = TestInit1.CreateSessionRequestEx(accessToken);
            var sessionResponseEx2 = await accessController.Session_Create(TestInit1.ServerId_1, sessionRequestEx2);
            baseResponse = await accessController.Session_AddUsage(TestInit1.ServerId_1, sessionResponseEx2.SessionId, new UsageInfo()
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });

            Assert.AreEqual(5, baseResponse.AccessUsage?.SentTraffic);
            Assert.AreEqual(10, baseResponse.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, baseResponse.ErrorCode);

            accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, sessionRequestEx2.TokenId, sessionRequestEx2.ClientInfo.ClientId);
            Assert.AreEqual(5, accessUsage.TotalSentTraffic);
            Assert.AreEqual(10, accessUsage.TotalReceivedTraffic);
            
            //-------------
            // check: add usage to client 1 after cycle
            //-------------

            //remove last cycle
            using VhContext vhContext = new();
            await PublicCycleHelper.DeleteCycle(PublicCycleHelper.CurrentCycleId);
            await PublicCycleHelper.UpdateCycle();

            baseResponse = await accessController.Session_AddUsage(TestInit1.ServerId_1, sessionResponseEx2.SessionId, new UsageInfo()
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });
            Assert.AreEqual(5, baseResponse.AccessUsage?.SentTraffic);
            Assert.AreEqual(10, baseResponse.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, baseResponse.ErrorCode);

            //-------------
            // check: GetAccess should return same result
            //-------------
            var sessionResponseEx1B = await accessController.Session_Create(TestInit1.ServerId_1, sessionRequestEx1);
            Assert.AreEqual(5, sessionResponseEx1B.AccessUsage?.SentTraffic);
            Assert.AreEqual(10, sessionResponseEx1B.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx1B.ErrorCode);

            baseResponse = await accessController.Session_AddUsage(TestInit1.ServerId_1, sessionResponseEx2.SessionId, new UsageInfo()
            {
                SentTraffic = 50,
                ReceivedTraffic = 100
            });
            accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, sessionRequestEx2.TokenId, sessionRequestEx2.ClientInfo.ClientId);
            Assert.AreEqual(50, accessUsage.CycleSentTraffic);
            Assert.AreEqual(100, accessUsage.CycleReceivedTraffic);

        }

        [TestMethod]
        public async Task AddUsage_Private()
        {
            // create token
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, new() { AccessTokenGroupId = TestInit1.AccessTokenGroupId_1, IsPublic = false });

            var accessController = TestInit1.CreateAccessController();
            var accessRequest1 = TestInit1.CreateSessionRequestEx(accessToken);
            var sessionResponseEx1 = await accessController.Session_Create(TestInit1.ServerId_1, accessRequest1);

            //--------------
            // check: zero usage
            //--------------
            var response = await accessController.Session_AddUsage(TestInit1.ServerId_1, sessionResponseEx1.SessionId, new UsageInfo()
            {
                SentTraffic = 0,
                ReceivedTraffic = 0
            });
            Assert.AreEqual(0, response.AccessUsage?.SentTraffic);
            Assert.AreEqual(0, response.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx1.ErrorCode);

            var accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, accessRequest1.TokenId, accessRequest1.ClientInfo.ClientId);
            Assert.AreEqual(0, accessUsage.TotalSentTraffic);
            Assert.AreEqual(0, accessUsage.TotalReceivedTraffic);

            //-----------
            // check: add usage by client 1
            //-----------
            response = await accessController.Session_AddUsage(TestInit1.ServerId_1, sessionResponseEx1.SessionId, new UsageInfo()
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });
            Assert.AreEqual(5, response.AccessUsage?.SentTraffic);
            Assert.AreEqual(10, response.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx1.ErrorCode);

            accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, accessRequest1.TokenId, accessRequest1.ClientInfo.ClientId);
            Assert.AreEqual(5, accessUsage.TotalSentTraffic);
            Assert.AreEqual(10, accessUsage.TotalReceivedTraffic);

            // again by client 2
            var sessionRequestEx2 = TestInit1.CreateSessionRequestEx(accessToken);
            var sessionResponseEx2 = await accessController.Session_Create(TestInit1.ServerId_1, sessionRequestEx2);
            var response2 = await accessController.Session_AddUsage(TestInit1.ServerId_1, sessionResponseEx2.SessionId, new UsageInfo()
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });

            Assert.AreEqual(10, response2.AccessUsage?.SentTraffic);
            Assert.AreEqual(20, response2.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, response2.ErrorCode);

            accessUsage = await accessTokenController.GetAccessUsage(TestInit1.ProjectId, sessionRequestEx2.TokenId, sessionRequestEx2.ClientInfo.ClientId);
            Assert.AreEqual(10, accessUsage.TotalSentTraffic);
            Assert.AreEqual(20, accessUsage.TotalReceivedTraffic);
        }

        [TestMethod]
        public async Task GetCertificateData()
        {
            // create new ServerEndPoint
            var dnsName = $"CN=fifoo-{Guid.NewGuid():N}.com";
            var serverEndPointController = TestInit.CreateServerEndPointController();
            var publicEndPointId = TestInit1.HostEndPoint_New1.ToString();
            await serverEndPointController.Create(TestInit1.ProjectId, publicEndPoint: publicEndPointId,
                new() { SubjectName = dnsName });

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
                await accessController.GetSslCertificateData(TestInit1.ServerId_1, TestInit1.HostEndPoint_New2.ToString());
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
            Assert.AreEqual(serverData1.Status?.SessionCount, 10);

            var serverData2 = await serverController.Get(TestInit1.ProjectId, serverId2);
            Assert.AreEqual(serverData2.Status?.SessionCount, 20);
        }

        [TestMethod]
        public async Task AccessUsageLog_Inserted()
        {
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessController = TestInit1.CreateAccessController();

            // create token
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, new() { });
            var sessionRequestEx = TestInit1.CreateSessionRequestEx(accessToken);
            sessionRequestEx.ClientInfo.ClientVersion = "2.0.2.0";
            sessionRequestEx.ClientInfo.UserAgent = "userAgent1";
            var sessionResponseEx = await accessController.Session_Create(TestInit1.ServerId_1, sessionRequestEx);

            //-----------
            // check: add usage
            //-----------
            await accessController.Session_AddUsage(TestInit1.ServerId_1, sessionResponseEx.SessionId, new UsageInfo() { SentTraffic = 10051, ReceivedTraffic = 20051 });
            await accessController.Session_AddUsage(TestInit1.ServerId_1, sessionResponseEx.SessionId, new UsageInfo() { SentTraffic = 20, ReceivedTraffic = 30 });

            // query database for usage
            var accessUsageLogs = await accessTokenController.GetAccessUsageLogs(TestInit1.ProjectId, accessTokenId: accessToken.AccessTokenId, clientId: sessionRequestEx.ClientInfo.ClientId, recordCount: 100);
            var accessUsageLog = accessUsageLogs[0];

            Assert.IsNotNull(accessUsageLog.Session);
            Assert.IsNotNull(accessUsageLog.Session.Client);
            Assert.AreEqual(accessToken.AccessTokenId, accessUsageLog.Session.AccessUsage?.AccessTokenId);
            Assert.AreEqual(sessionRequestEx.ClientInfo.ClientId, accessUsageLog.Session.Client.UserClientId);
            Assert.AreEqual(sessionRequestEx.ClientIp?.ToString(), accessUsageLog.Session.ClientIp);
            Assert.AreEqual(sessionRequestEx.ClientInfo.ClientVersion, accessUsageLog.Session.ClientVersion);
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
