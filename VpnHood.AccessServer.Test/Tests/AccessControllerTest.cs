using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.Common.Messaging;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class AccessControllerTest : ControllerTest
    {
        [TestMethod]
        public async Task Session_Create_Status_Expired()
        {
            var accessTokenController = TestInit1.CreateAccessTokenController();

            // create accessToken
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId,
                new AccessTokenCreateParams
                {
                    AccessPointGroupId = TestInit1.AccessPointGroupId1,
                    EndTime = new DateTime(1900, 1, 1)
                });
            var accessController = TestInit1.CreateAccessController();

            var sessionResponseEx = await accessController.Session_Create(TestInit1.CreateSessionRequestEx(accessToken));
            Assert.AreEqual(SessionErrorCode.AccessExpired, sessionResponseEx.ErrorCode);
        }

        [TestMethod]
        public async Task Session_Create_Status_TrafficOverflow()
        {
            var accessTokenController = TestInit1.CreateAccessTokenController();

            // create accessToken
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId,
                new AccessTokenCreateParams
                {
                    AccessPointGroupId = TestInit1.AccessPointGroupId1,
                    MaxTraffic = 14
                });

            // get access
            var accessController = TestInit1.CreateAccessController();
            var sessionResponseEx = await accessController.Session_Create(TestInit1.CreateSessionRequestEx(accessToken));

            //-----------
            // check: add usage
            //-----------
            var sessionResponse = await accessController.Session_AddUsage(sessionResponseEx.SessionId, closeSession: false, usageInfo: new UsageInfo
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });
            Assert.AreEqual(5, sessionResponse.AccessUsage?.SentTraffic);
            Assert.AreEqual(10, sessionResponse.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.AccessTrafficOverflow, sessionResponse.ErrorCode);
        }

        [TestMethod]
        public async Task Session_Create_Status_No_TrafficOverflow_when_maxTraffic_is_zero()
        {
            var accessTokenController = TestInit1.CreateAccessTokenController();

            // create accessToken
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId,
                new AccessTokenCreateParams
                {
                    AccessPointGroupId = TestInit1.AccessPointGroupId1,
                    MaxTraffic = 0
                });
            var accessController = TestInit1.CreateAccessController();

            //-----------
            // check: add usage
            //-----------
            var sessionResponseEx =
                await accessController.Session_Create(TestInit1.CreateSessionRequestEx(accessToken));
            var sessionResponse = await accessController.Session_AddUsage(sessionResponseEx.SessionId, new UsageInfo
            {
                SentTraffic = 5,
                ReceivedTraffic = 10
            });
            Assert.AreEqual(5, sessionResponse.AccessUsage?.SentTraffic);
            Assert.AreEqual(10, sessionResponse.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, sessionResponse.ErrorCode);
        }

        [TestMethod]
        public async Task Session_Create_set_expirationTime_first_use()
        {
            var accessTokenController = TestInit1.CreateAccessTokenController();

            // create token
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId,
                new AccessTokenCreateParams
                {
                    AccessPointGroupId = TestInit1.AccessPointGroupId1,
                    EndTime = null,
                    Lifetime = 30
                });
            var accessController = TestInit1.CreateAccessController();

            //-----------
            // check: add usage
            //-----------
            var sessionResponseEx =
                await accessController.Session_Create(TestInit1.CreateSessionRequestEx(accessToken));
            Assert.IsNotNull(sessionResponseEx.AccessUsage);
            Assert.AreEqual(0, sessionResponseEx.AccessUsage.SentTraffic);
            Assert.AreEqual(0, sessionResponseEx.AccessUsage.ReceivedTraffic);
            Assert.IsNotNull(sessionResponseEx.AccessUsage.ExpirationTime);
            Assert.IsTrue((sessionResponseEx.AccessUsage.ExpirationTime.Value - DateTime.UtcNow.AddDays(30)).TotalSeconds < 10);
            Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode);
        }

        [TestMethod]
        public async Task Session_Get_should_update_accessedTime()
        {
            // create token
            var accessTokenController = TestInit1.CreateAccessTokenController();
            TestInit1.CreateAccessController();
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId,
                new AccessTokenCreateParams
                {
                    AccessPointGroupId = TestInit1.AccessPointGroupId1
                });

            // create a session for token
            var accessController = TestInit1.CreateAccessController();
            var sessionRequestEx = TestInit1.CreateSessionRequestEx(accessToken);
            var sessionResponseEx = await accessController.Session_Create(sessionRequestEx);

            // get the token again
            var sessionResponseEx2 = await accessController.Session_Get(
                sessionResponseEx.SessionId, sessionRequestEx.HostEndPoint.ToString(), null);
            Assert.AreEqual(sessionResponseEx.ErrorCode, sessionResponseEx2.ErrorCode);
            Assert.AreEqual(sessionResponseEx.SessionId, sessionResponseEx2.SessionId);
            CollectionAssert.AreEqual(sessionResponseEx.SessionKey, sessionResponseEx2.SessionKey);
        }

        [TestMethod]
        public async Task Session_Create_should_not_reset_expiration_Time()
        {
            var expectedExpirationTime = DateTime.UtcNow.AddDays(10).Date;

            // create token
            var accessTokenController = TestInit1.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId,
                new AccessTokenCreateParams
                {
                    AccessPointGroupId = TestInit1.AccessPointGroupId2,
                    EndTime = expectedExpirationTime,
                    Lifetime = 30
                });

            var accessController = TestInit1.CreateAccessController();
            var sessionResponseEx = await accessController.Session_Create(TestInit1.CreateSessionRequestEx(accessToken, hostEndPoint: TestInit1.HostEndPointG2S1));
            Assert.AreEqual(expectedExpirationTime, sessionResponseEx.AccessUsage?.ExpirationTime);
            Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx.ErrorCode);
        }


        [TestMethod]
        public async Task Session_Create()
        {
            // create token
            var accessTokenController = TestInit1.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId,
                new AccessTokenCreateParams
                {
                    AccessPointGroupId = TestInit1.AccessPointGroupId1,
                    MaxTraffic = 100,
                    EndTime = new DateTime(2040, 1, 1),
                    Lifetime = 0,
                    MaxClient = 22
                });

            var beforeUpdateTime = DateTime.UtcNow;
            var sessionRequestEx = TestInit1.CreateSessionRequestEx(accessToken,
                hostEndPoint: TestInit1.HostEndPointG1S1, clientIp: TestInit1.ClientIp1);
            sessionRequestEx.ClientInfo.UserAgent = "userAgent1";
            sessionRequestEx.ClientInfo.ClientVersion = "1.0.0";
            var clientInfo = sessionRequestEx.ClientInfo;

            var accessController = TestInit1.CreateAccessController();
            var sessionResponseEx = await accessController.Session_Create(sessionRequestEx);

            Assert.IsNotNull(sessionResponseEx.AccessUsage);
            Assert.IsTrue(sessionResponseEx.SessionId > 0);
            Assert.AreEqual(new DateTime(2040, 1, 1), sessionResponseEx.AccessUsage.ExpirationTime);
            Assert.AreEqual(22, sessionResponseEx.AccessUsage.MaxClientCount);
            Assert.AreEqual(100, sessionResponseEx.AccessUsage.MaxTraffic);
            Assert.AreEqual(0, sessionResponseEx.AccessUsage.ReceivedTraffic);
            Assert.AreEqual(0, sessionResponseEx.AccessUsage.SentTraffic);
            Assert.IsNotNull(sessionResponseEx.SessionKey);

            // check ProjectClient id and its properties are created 
            var clientController = TestInit1.CreateClientController();
            var client = await clientController.Get(TestInit1.ProjectId, clientInfo.ClientId);
            Assert.AreEqual(clientInfo.ClientId, client.ClientId);
            Assert.AreEqual(clientInfo.UserAgent, client.UserAgent);
            Assert.AreEqual(clientInfo.ClientVersion, client.ClientVersion);

            // check created time
            var access = await accessTokenController.GetAccess(TestInit1.ProjectId, sessionRequestEx.TokenId,
                clientInfo.ClientId);
            Assert.IsTrue(access.CreatedTime >= beforeUpdateTime);
            Assert.IsTrue(access.ModifiedTime >= beforeUpdateTime);

            // check updating same client
            beforeUpdateTime = DateTime.UtcNow;
            sessionRequestEx.ClientIp = TestInit1.ClientIp2;
            sessionRequestEx.ClientInfo.UserAgent = "userAgent2";
            sessionRequestEx.ClientInfo.ClientVersion = "2.0.0";
            await accessController.Session_Create(sessionRequestEx);
            client = await clientController.Get(TestInit1.ProjectId, sessionRequestEx.ClientInfo.ClientId);
            Assert.AreEqual(clientInfo.UserAgent, client.UserAgent);
            Assert.AreEqual(clientInfo.ClientVersion, client.ClientVersion);

            access = await accessTokenController.GetAccess(TestInit1.ProjectId, sessionRequestEx.TokenId, clientInfo.ClientId);
            Assert.IsTrue(access.ModifiedTime >= beforeUpdateTime);
        }

        [TestMethod]
        public async Task Session_Create_Data_Unauthorized_EndPoint()
        {
            var accessTokenController = TestInit1.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId,
                new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1 });

            // create first public token
            var accessController = TestInit1.CreateAccessController();

            //-----------
            // check: access should grant to public token 1 by another public endpoint
            //-----------
            var sessionRequestEx = await accessController.Session_Create(
                TestInit1.CreateSessionRequestEx(accessToken, hostEndPoint: TestInit1.HostEndPointG1S2));
            Assert.AreEqual(SessionErrorCode.Ok, sessionRequestEx.ErrorCode);

            //-----------
            // check: access should not grant to public token 1 by private server endpoint
            //-----------
            try
            {
                await accessController.Session_Create(TestInit1.CreateSessionRequestEx(accessToken, hostEndPoint: TestInit1.HostEndPointG2S1));
                Assert.Fail("Exception expected");
            }
            catch (Exception ex) when (AccessUtil.IsNotExistsException(ex))
            {
            }
        }

        [TestMethod]
        public async Task Session_AddUsage_Public()
        {
            // create token
            var accessTokenController = TestInit1.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId,
                new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1, IsPublic = true });

            var accessController = TestInit1.CreateAccessController();
            var sessionRequestEx1 = TestInit1.CreateSessionRequestEx(accessToken);
            var sessionResponseEx1 = await accessController.Session_Create(sessionRequestEx1);

            //--------------
            // check: zero usage
            //--------------
            var baseResponse = await accessController.Session_AddUsage(
                sessionResponseEx1.SessionId, new UsageInfo
                {
                    SentTraffic = 0,
                    ReceivedTraffic = 0
                });
            Assert.AreEqual(0, baseResponse.AccessUsage?.SentTraffic);
            Assert.AreEqual(0, baseResponse.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, baseResponse.ErrorCode);

            var access = await accessTokenController.GetAccess(TestInit1.ProjectId, sessionRequestEx1.TokenId,
                sessionRequestEx1.ClientInfo.ClientId);
            Assert.AreEqual(0, access.TotalSentTraffic);
            Assert.AreEqual(0, access.TotalReceivedTraffic);

            //-----------
            // check: add usage
            //-----------
            baseResponse = await accessController.Session_AddUsage(sessionResponseEx1.SessionId,
                new UsageInfo
                {
                    SentTraffic = 5,
                    ReceivedTraffic = 10
                });
            Assert.AreEqual(5, baseResponse.AccessUsage?.SentTraffic);
            Assert.AreEqual(10, baseResponse.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, baseResponse.ErrorCode);

            access = await accessTokenController.GetAccess(TestInit1.ProjectId, sessionRequestEx1.TokenId,
                sessionRequestEx1.ClientInfo.ClientId);
            Assert.AreEqual(5, access.TotalSentTraffic);
            Assert.AreEqual(10, access.TotalReceivedTraffic);

            // again
            baseResponse = await accessController.Session_AddUsage(sessionResponseEx1.SessionId,
                new UsageInfo
                {
                    SentTraffic = 5,
                    ReceivedTraffic = 10
                });

            Assert.AreEqual(10, baseResponse.AccessUsage?.SentTraffic);
            Assert.AreEqual(20, baseResponse.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, baseResponse.ErrorCode);

            access = await accessTokenController.GetAccess(TestInit1.ProjectId, sessionRequestEx1.TokenId,
                sessionRequestEx1.ClientInfo.ClientId);
            Assert.AreEqual(10, access.TotalSentTraffic);
            Assert.AreEqual(20, access.TotalReceivedTraffic);

            //-----------
            // check: add usage for client 2
            //-----------
            var sessionRequestEx2 = TestInit1.CreateSessionRequestEx(accessToken);
            var sessionResponseEx2 = await accessController.Session_Create(sessionRequestEx2);
            baseResponse = await accessController.Session_AddUsage(sessionResponseEx2.SessionId,
                new UsageInfo
                {
                    SentTraffic = 5,
                    ReceivedTraffic = 10
                });

            Assert.AreEqual(5, baseResponse.AccessUsage?.SentTraffic);
            Assert.AreEqual(10, baseResponse.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, baseResponse.ErrorCode);

            access = await accessTokenController.GetAccess(TestInit1.ProjectId, sessionRequestEx2.TokenId,
                sessionRequestEx2.ClientInfo.ClientId);
            Assert.AreEqual(5, access.TotalSentTraffic);
            Assert.AreEqual(10, access.TotalReceivedTraffic);

            //-------------
            // check: add usage to client 1 after cycle
            //-------------

            //remove last cycle
            await using var vhContext = new VhContext();
            await PublicCycleHelper.DeleteCycle(PublicCycleHelper.CurrentCycleId);
            await PublicCycleHelper.UpdateCycle();

            baseResponse = await accessController.Session_AddUsage(sessionResponseEx2.SessionId,
                new UsageInfo
                {
                    SentTraffic = 5,
                    ReceivedTraffic = 10
                });
            Assert.AreEqual(5, baseResponse.AccessUsage?.SentTraffic);
            Assert.AreEqual(10, baseResponse.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, baseResponse.ErrorCode);

            //-------------
            // check: Session_Create for another client should return same result
            //-------------
            sessionResponseEx2 = await accessController.Session_Create(sessionRequestEx2);
            Assert.AreEqual(5, sessionResponseEx2.AccessUsage?.SentTraffic);
            Assert.AreEqual(10, sessionResponseEx2.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx2.ErrorCode);


            //-------------
            // check: Session for another client should be reset too
            //-------------
            await accessController.Session_AddUsage(sessionResponseEx1.SessionId,
                new UsageInfo
                {
                    SentTraffic = 50,
                    ReceivedTraffic = 100
                });
            access = await accessTokenController.GetAccess(TestInit1.ProjectId, sessionRequestEx1.TokenId,
                sessionRequestEx1.ClientInfo.ClientId);
            Assert.AreEqual(50, access.CycleSentTraffic);
            Assert.AreEqual(100, access.CycleReceivedTraffic);
        }

        [TestMethod]
        public async Task Session_AddUsage_Private()
        {
            // create token
            var accessTokenController = TestInit1.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId,
                new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1, IsPublic = false });

            var accessController = TestInit1.CreateAccessController();
            var sessionRequestEx1 = TestInit1.CreateSessionRequestEx(accessToken);
            var sessionResponseEx1 = await accessController.Session_Create(sessionRequestEx1);

            //--------------
            // check: zero usage
            //--------------
            var response = await accessController.Session_AddUsage(sessionResponseEx1.SessionId,
                new UsageInfo
                {
                    SentTraffic = 0,
                    ReceivedTraffic = 0
                });
            Assert.AreEqual(0, response.AccessUsage?.SentTraffic);
            Assert.AreEqual(0, response.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx1.ErrorCode);

            var access = await accessTokenController.GetAccess(TestInit1.ProjectId, sessionRequestEx1.TokenId,
                sessionRequestEx1.ClientInfo.ClientId);
            Assert.AreEqual(0, access.TotalSentTraffic);
            Assert.AreEqual(0, access.TotalReceivedTraffic);

            //-----------
            // check: add usage by client 1
            //-----------
            response = await accessController.Session_AddUsage(sessionResponseEx1.SessionId,
                new UsageInfo
                {
                    SentTraffic = 5,
                    ReceivedTraffic = 10
                });
            Assert.AreEqual(5, response.AccessUsage?.SentTraffic);
            Assert.AreEqual(10, response.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, sessionResponseEx1.ErrorCode);

            access = await accessTokenController.GetAccess(TestInit1.ProjectId, sessionRequestEx1.TokenId,
                sessionRequestEx1.ClientInfo.ClientId);
            Assert.AreEqual(5, access.TotalSentTraffic);
            Assert.AreEqual(10, access.TotalReceivedTraffic);

            // again by client 2
            var sessionRequestEx2 = TestInit1.CreateSessionRequestEx(accessToken);
            var sessionResponseEx2 = await accessController.Session_Create(sessionRequestEx2);
            var response2 = await accessController.Session_AddUsage(sessionResponseEx2.SessionId,
                new UsageInfo
                {
                    SentTraffic = 5,
                    ReceivedTraffic = 10
                });

            Assert.AreEqual(10, response2.AccessUsage?.SentTraffic);
            Assert.AreEqual(20, response2.AccessUsage?.ReceivedTraffic);
            Assert.AreEqual(SessionErrorCode.Ok, response2.ErrorCode);

            access = await accessTokenController.GetAccess(TestInit1.ProjectId, sessionRequestEx2.TokenId,
                sessionRequestEx2.ClientInfo.ClientId);
            Assert.AreEqual(10, access.TotalSentTraffic);
            Assert.AreEqual(20, access.TotalReceivedTraffic);
        }

        [TestMethod]
        public async Task GetCertificateData()
        {
            // create new AccessPoint
            var privateIp = await TestInit.NewIp();
            var publicIp = await TestInit.NewIp();
            var tcpPort = 443;
            var privateEndPoint = new IPEndPoint(privateIp, tcpPort);
            var accessPointController = TestInit1.CreateAccessPointController();
            await accessPointController.Create(TestInit1.ProjectId, TestInit1.ServerId1,
                new AccessPointCreateParams
                {
                    AccessPointGroupId = TestInit1.AccessPointGroupId1,
                    PublicIpAddress = publicIp,
                    PrivateIpAddress = privateIp,
                    TcpPort = tcpPort
                });


            //-----------
            // check: get certificate by publicIp
            //-----------
            var accessController = TestInit1.CreateAccessController();
            var certBuffer = await accessController.GetSslCertificateData(privateEndPoint.ToString());
            var certificate = new X509Certificate2(certBuffer);
            Assert.AreEqual(TestInit1.PublicServerDns, certificate.GetNameInfo(X509NameType.DnsName, false));

            //-----------
            // check: get certificate by privateIp
            //-----------
            certBuffer = await accessController.GetSslCertificateData(privateEndPoint.ToString());
            certificate = new X509Certificate2(certBuffer);
            Assert.AreEqual(TestInit1.PublicServerDns, certificate.GetNameInfo(X509NameType.DnsName, false));

            //-----------
            // check: check not found
            //-----------
            try
            {
                var newIp = await TestInit.NewIp();
                await accessController.GetSslCertificateData(newIp.ToString());
                Assert.Fail("NotExistsException expected!");
            }
            catch (Exception ex) when (AccessUtil.IsNotExistsException(ex))
            {
            }
        }

        [TestMethod]
        public async Task SendServerStatus()
        {
            var accessController1 = TestInit1.CreateAccessController(TestInit1.ServerId1);
            await accessController1.SendServerStatus(new ServerStatus { SessionCount = 10 });

            var accessController2 = TestInit1.CreateAccessController(TestInit1.ServerId2);
            await accessController2.SendServerStatus(new ServerStatus { SessionCount = 20 });

            var serverController = TestInit1.CreateServerController();

            var serverData1 = await serverController.Get(TestInit1.ProjectId, TestInit1.ServerId1);
            Assert.AreEqual(serverData1.Status?.SessionCount, 10);

            var serverData2 = await serverController.Get(TestInit1.ProjectId, TestInit1.ServerId2);
            Assert.AreEqual(serverData2.Status?.SessionCount, 20);
        }

        [TestMethod]
        public async Task AccessLog_Inserted()
        {
            var accessTokenController = TestInit1.CreateAccessTokenController();
            var accessController = TestInit1.CreateAccessController();

            // create token
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1 });
            var sessionRequestEx = TestInit1.CreateSessionRequestEx(accessToken);
            sessionRequestEx.ClientInfo.ClientVersion = "2.0.2.0";
            sessionRequestEx.ClientInfo.UserAgent = "userAgent1";
            var sessionResponseEx = await accessController.Session_Create(sessionRequestEx);

            //-----------
            // check: add usage
            //-----------
            await accessController.Session_AddUsage(sessionResponseEx.SessionId,
                new UsageInfo { SentTraffic = 10051, ReceivedTraffic = 20051 });
            await accessController.Session_AddUsage(sessionResponseEx.SessionId,
                new UsageInfo { SentTraffic = 20, ReceivedTraffic = 30 });

            // query database for usage
            var accessLogs = await accessTokenController.GetAccessLogs(TestInit1.ProjectId,
                accessToken.AccessTokenId, sessionRequestEx.ClientInfo.ClientId, recordCount: 100);
            var accessLog = accessLogs[0];

            Assert.IsNotNull(accessLog.Session);
            Assert.IsNotNull(accessLog.Session.Client);
            Assert.AreEqual(accessToken.AccessTokenId, accessLog.Session.Access?.AccessTokenId);
            Assert.AreEqual(sessionRequestEx.ClientInfo.ClientId, accessLog.Session.Client.ClientId);
            Assert.AreEqual(sessionRequestEx.ClientIp?.ToString(), accessLog.Session.ClientIp);
            Assert.AreEqual(sessionRequestEx.ClientInfo.ClientVersion, accessLog.Session.ClientVersion);
            Assert.AreEqual(20, accessLog.SentTraffic);
            Assert.AreEqual(30, accessLog.ReceivedTraffic);
            Assert.AreEqual(10071, accessLog.CycleSentTraffic);
            Assert.AreEqual(20081, accessLog.CycleReceivedTraffic);
            Assert.AreEqual(10071, accessLog.TotalSentTraffic);
            Assert.AreEqual(20081, accessLog.TotalReceivedTraffic);
            Assert.AreEqual(TestInit1.ServerId1, accessLog.ServerId);
        }
    }
}