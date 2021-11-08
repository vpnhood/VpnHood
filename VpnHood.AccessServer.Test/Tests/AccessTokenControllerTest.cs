using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.DTOs;
using VpnHood.Common;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class AccessTokenControllerTest : ControllerTest
    {
        [TestMethod]
        public async Task SupportCode_is_unique_per_project()
        {
            var accessTokenController1 = TestInit1.CreateAccessTokenController();
            var accessToken11 = await accessTokenController1.Create(TestInit1.ProjectId, new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1 });
            
            var accessTokenController2 = TestInit2.CreateAccessTokenController();
            var accessToken21 = await accessTokenController2.Create(TestInit2.ProjectId, new AccessTokenCreateParams { AccessPointGroupId = TestInit2.AccessPointGroupId1 });

            var accessToken12 = await accessTokenController1.Create(TestInit1.ProjectId, new AccessTokenCreateParams { AccessPointGroupId = TestInit1.AccessPointGroupId1 });
            var accessToken22 = await accessTokenController2.Create(TestInit2.ProjectId, new AccessTokenCreateParams { AccessPointGroupId = TestInit2.AccessPointGroupId1 });

            Assert.AreEqual(accessToken11.SupportCode + 1, accessToken12.SupportCode);
            Assert.AreEqual(accessToken21.SupportCode + 1, accessToken22.SupportCode);
        }

        [TestMethod]
        public async Task CRUD()
        {
            //-----------
            // check: create
            //-----------
            var accessTokenController = TestInit1.CreateAccessTokenController();

            var endTime1 = DateTime.Today.AddDays(1);
            endTime1 = endTime1.AddMilliseconds(-endTime1.Millisecond);

            var accessToken1 = await accessTokenController.Create(TestInit1.ProjectId, new AccessTokenCreateParams
            {
                AccessPointGroupId = TestInit1.AccessPointGroupId1,
                AccessTokenName = "tokenName1",
                Url = "https://foo.com/accessKey1",
                MaxTraffic = 11,
                MaxClient = 12,
                Lifetime = 13,
                EndTime = endTime1
            });
            Assert.AreNotEqual(0, accessToken1.SupportCode);
            Assert.AreEqual("tokenName1", accessToken1.AccessTokenName);
            Assert.AreEqual(TestInit1.AccessPointGroupId1, accessToken1.AccessPointGroupId);
            Assert.IsNull(accessToken1.StartTime);
            Assert.AreEqual(endTime1, accessToken1.EndTime);
            Assert.AreEqual(11, accessToken1.MaxTraffic);
            Assert.AreEqual(12, accessToken1.MaxClient);
            Assert.AreEqual(13, accessToken1.Lifetime);
            Assert.AreEqual("https://foo.com/accessKey1", accessToken1.Url);

            var endTime2 = DateTime.UtcNow.AddDays(2);
            var accessToken2A = await accessTokenController.Create(TestInit1.ProjectId, new AccessTokenCreateParams
            {
                AccessPointGroupId = TestInit1.AccessPointGroupId2,
                AccessTokenName = "tokenName2",
                Url = "https://foo.com/accessKey2",
                MaxTraffic = 21,
                MaxClient = 22,
                Lifetime = 23,
                EndTime = endTime2,
                IsPublic = true
            });
            Assert.AreNotEqual(0, accessToken2A.SupportCode);
            Assert.AreEqual("tokenName2", accessToken2A.AccessTokenName);
            Assert.AreEqual(TestInit1.AccessPointGroupId2, accessToken2A.AccessPointGroupId);
            Assert.IsNull(accessToken2A.StartTime);
            Assert.AreEqual(endTime2, accessToken2A.EndTime);
            Assert.AreEqual(21, accessToken2A.MaxTraffic);
            Assert.AreEqual(22, accessToken2A.MaxClient);
            Assert.AreEqual(23, accessToken2A.Lifetime);
            Assert.AreEqual("https://foo.com/accessKey2", accessToken2A.Url);

            //-----------
            // check: get
            //-----------
            var accessToken2B = (await accessTokenController.Get(TestInit1.ProjectId, accessToken2A.AccessTokenId))
                .AccessToken;
            Assert.AreEqual(accessToken2A.EndTime?.ToString("dd-MM-yyyy hh:mm:ss"), accessToken2B.EndTime?.ToString("dd-MM-yyyy hh:mm:ss"));
            Assert.AreEqual(accessToken2A.AccessTokenId, accessToken2B.AccessTokenId);
            Assert.AreEqual(accessToken2A.AccessPointGroupId, accessToken2B.AccessPointGroupId);
            Assert.AreEqual(accessToken2A.AccessTokenName, accessToken2B.AccessTokenName);
            Assert.AreEqual(accessToken2A.ProjectId, accessToken2B.ProjectId);
            Assert.AreEqual(accessToken2A.IsPublic, accessToken2B.IsPublic);
            Assert.AreEqual(accessToken2A.Lifetime, accessToken2B.Lifetime);
            Assert.AreEqual(accessToken2A.MaxClient, accessToken2B.MaxClient);
            Assert.AreEqual(accessToken2A.StartTime, accessToken2B.StartTime);
            Assert.AreEqual(accessToken2A.SupportCode, accessToken2B.SupportCode);
            Assert.AreEqual(accessToken2A.Url, accessToken2B.Url);
            CollectionAssert.AreEqual(accessToken2A.Secret, accessToken2B.Secret);

            //-----------
            // check: update
            //-----------
            AccessTokenUpdateParams updateParams = new()
            {
                AccessTokenName = $"new_name_{Guid.NewGuid()}",
                AccessPointGroupId = accessToken2A.AccessPointGroupId,
                EndTime = DateTime.UtcNow.AddDays(4),
                Lifetime = 61,
                MaxClient = 7,
                MaxTraffic = 805004,
                Url = "http:" + $"//www.sss.com/new{Guid.NewGuid()}.com"
            };

            await accessTokenController.Update(TestInit1.ProjectId, accessToken2A.AccessTokenId, updateParams);
            accessToken2B = (await accessTokenController.Get(TestInit1.ProjectId, accessToken2A.AccessTokenId))
                .AccessToken;

            Assert.AreEqual(updateParams.EndTime.Value?.ToString("dd-MM-yyyy hh:mm:ss"),
                accessToken2B.EndTime?.ToString("dd-MM-yyyy hh:mm:ss"));
            Assert.AreEqual(accessToken2A.AccessTokenId, accessToken2B.AccessTokenId);
            Assert.AreEqual(updateParams.AccessPointGroupId, accessToken2B.AccessPointGroupId);
            Assert.AreEqual(updateParams.AccessTokenName, accessToken2B.AccessTokenName);
            Assert.AreEqual(accessToken2A.ProjectId, accessToken2B.ProjectId);
            Assert.AreEqual(accessToken2A.IsPublic, accessToken2B.IsPublic);
            Assert.AreEqual(updateParams.Lifetime, accessToken2B.Lifetime);
            Assert.AreEqual(updateParams.MaxClient, accessToken2B.MaxClient);
            Assert.AreEqual(accessToken2A.StartTime, accessToken2B.StartTime);
            Assert.AreEqual(accessToken2A.SupportCode, accessToken2B.SupportCode);
            Assert.AreEqual(updateParams.Url, accessToken2B.Url);

            //-----------
            // check: getAccessKey
            //-----------
            var agentController = TestInit1.CreateAgentController();
            var certificateData = await agentController.GetSslCertificateData(TestInit1.HostEndPointG2S1.ToString());
            var x509Certificate2 = new X509Certificate2(certificateData);

            var accessKey = await accessTokenController.GetAccessKey(TestInit1.ProjectId, accessToken2B.AccessTokenId);
            var token = Token.FromAccessKey(accessKey);
            Assert.AreEqual(x509Certificate2.GetNameInfo(X509NameType.DnsName, false), token.HostName);
            Assert.AreEqual(true, token.IsPublic);
            Assert.AreEqual(accessToken2B.AccessTokenName, token.Name);
            Assert.AreEqual(Convert.ToBase64String(x509Certificate2.GetCertHash()), Convert.ToBase64String(token.CertificateHash));
            Assert.AreEqual(Convert.ToBase64String(accessToken2B.Secret), Convert.ToBase64String(token.Secret));
            Assert.IsFalse(token.HostEndPoints?.Any(x=>x.Equals(TestInit1.HostEndPointG1S1)));
            Assert.IsTrue(token.HostEndPoints?.Any(x=>x.Equals(TestInit1.HostEndPointG2S2)));
            Assert.AreEqual(accessToken2B.SupportCode, token.SupportId);

            //-----------
            // Check: getAccessKey
            //-----------
            await accessTokenController.Delete(accessToken2B.ProjectId, accessToken2B.AccessTokenId);
            try
            {
                await accessTokenController.Get(TestInit1.ProjectId, accessToken2A.AccessTokenId);
                Assert.Fail("AccessToken should not exist!");
            }
            catch (Exception ex) when (AccessUtil.IsNotExistsException(ex)) { }
            {
            }

        }

        [TestMethod]
        public async Task GetAccessUsageLogs()
        {
            var agentController = TestInit1.CreateAgentController();
            var sessionRequestEx = TestInit1.CreateSessionRequestEx();

            var session = await agentController.Session_Create(sessionRequestEx);

            // add usage
            var dateTime = DateTime.UtcNow;
            UsageInfo usageInfo = new()
            {
                ReceivedTraffic = 1000 * 1000000,
                SentTraffic = 1000 * 1000000
            };
            await Task.Delay(500);
            await agentController.Session_AddUsage(session.SessionId, closeSession: false,
                usageInfo: usageInfo);

            // get usage
            var accessTokenController = TestInit1.CreateAccessTokenController();
            var usageLogs = await accessTokenController.GetAccessLogs(TestInit1.ProjectId,
                TestInit1.AccessToken1.AccessTokenId, sessionRequestEx.ClientInfo.ClientId);
            var usageLog = usageLogs.Single();
            Assert.IsNotNull(usageLog.Session);
            Assert.IsNotNull(usageLog.Session.Client);
            Assert.AreEqual(sessionRequestEx.ClientIp?.ToString(), usageLog.Session.ClientIp);
            Assert.AreEqual(sessionRequestEx.ClientInfo.ClientVersion, usageLog.Session.Client.ClientVersion); //make sure client is returned
            Assert.AreEqual(sessionRequestEx.ClientInfo.ClientId, usageLog.Session.Client.ClientId);
            Assert.AreEqual(sessionRequestEx.ClientInfo.ClientVersion, usageLog.Session.ClientVersion);
            Assert.AreEqual(usageInfo.ReceivedTraffic, usageLog.ReceivedTraffic);
            Assert.AreEqual(usageInfo.SentTraffic, usageLog.SentTraffic);
            Assert.IsTrue(dateTime <= usageLog.CreatedTime);
        }

        [TestMethod]
        public async Task Validate_create()
        {
            var accessTokenController = TestInit1.CreateAccessTokenController();
            try
            {
                await accessTokenController.Create(TestInit1.ProjectId,
                    new AccessTokenCreateParams { AccessPointGroupId = TestInit2.AccessPointGroupId1 });
                Assert.Fail("KeyNotFoundException is expected!");
            }
            catch (Exception ex) when (AccessUtil.IsNotExistsException(ex))
            {
            }
        }

        [TestMethod]
        public async Task Validate_update()
        {
            var accessTokenController = TestInit1.CreateAccessTokenController();
            try
            {
                await accessTokenController.Update(TestInit1.ProjectId, TestInit1.AccessToken1.AccessTokenId,
                    new AccessTokenUpdateParams { AccessPointGroupId = TestInit2.AccessPointGroupId1 });
                Assert.Fail("KeyNotFoundException is expected!");
            }
            catch (Exception ex) when (AccessUtil.IsNotExistsException(ex))
            {
            }
        }

        [TestMethod]
        public async Task List()
        {
            // create a new group with new server endpoint
            var accessPointGroupController = TestInit1.CreateAccessPointGroupController();
            var accessPointGroup =
                await accessPointGroupController.Create(TestInit1.ProjectId, null);
            var hostEndPoint = await TestInit.NewEndPoint();

            await TestInit1.CreateAccessPointController().Create(TestInit1.ProjectId, TestInit1.ServerId1,
                new AccessPointCreateParams(hostEndPoint.Address)
                {
                    AccessPointGroupId = accessPointGroup.AccessPointGroupId,
                    TcpPort = hostEndPoint.Port
                });

            var accessTokenControl = TestInit1.CreateAccessTokenController();
            var publicAccessToken = await accessTokenControl.Create(TestInit1.ProjectId,
                new AccessTokenCreateParams
                { AccessPointGroupId = accessPointGroup.AccessPointGroupId, IsPublic = true });
            var privateAccessToken = await accessTokenControl.Create(TestInit1.ProjectId,
                new AccessTokenCreateParams
                { AccessPointGroupId = accessPointGroup.AccessPointGroupId, IsPublic = false });

            // add usage
            var usageInfo = new UsageInfo { ReceivedTraffic = 10000000, SentTraffic = 10000000 };
            var agentController = TestInit1.CreateAgentController();
            var publicSessionResponseEx = await agentController.Session_Create(
                TestInit1.CreateSessionRequestEx(publicAccessToken, hostEndPoint: hostEndPoint));
            await agentController.Session_AddUsage(publicSessionResponseEx.SessionId,
                closeSession: false, usageInfo: usageInfo);

            var privateSessionResponseEx = await agentController.Session_Create(
                TestInit1.CreateSessionRequestEx(privateAccessToken, hostEndPoint: hostEndPoint));
            await agentController.Session_AddUsage(privateSessionResponseEx.SessionId,
                closeSession: false, usageInfo: usageInfo);

            // list
            var accessTokenController = TestInit1.CreateAccessTokenController();
            var accessTokens = await accessTokenController.List(TestInit1.ProjectId, accessPointGroup.AccessPointGroupId);
            var publicItem = accessTokens.First(x => x.AccessToken.IsPublic);
            var privateItem = accessTokens.First(x => !x.AccessToken.IsPublic);
            Assert.IsNull(publicItem.Access);
            Assert.AreEqual(usageInfo.ReceivedTraffic, privateItem.Access?.CycleReceivedTraffic);
            Assert.AreEqual(usageInfo.SentTraffic, privateItem.Access?.CycleSentTraffic);
        }
    }
}