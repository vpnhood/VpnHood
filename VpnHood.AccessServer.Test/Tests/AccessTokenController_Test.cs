using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using VpnHood.AccessServer.DTOs;
using VpnHood.Common;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class AccessTokenController_Test : ControllerTest
    {
        public async Task SupportCode_is_unique_per_project()
        {
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessToken1_1 = await accessTokenController.Create(TestInit1.ProjectId, new() { });
            var accessToken2_1 = await accessTokenController.Create(TestInit2.ProjectId, new() { });
            var accessToken1_2 = await accessTokenController.Create(TestInit1.ProjectId, new() { });
            var accessToken2_2 = await accessTokenController.Create(TestInit2.ProjectId, new() { });

            Assert.AreEqual(accessToken1_1.SupportCode, accessToken1_2.SupportCode + 1);
            Assert.AreEqual(accessToken2_1.SupportCode, accessToken2_2.SupportCode + 1);
        }

        [TestMethod]
        public async Task CRUD_public()
        {
            //-----------
            // check: create
            //-----------
            var accessTokenController = TestInit.CreateAccessTokenController();

            var endTime1 = DateTime.Today.AddDays(1);
            endTime1 = endTime1.AddMilliseconds(-endTime1.Millisecond);

            var accessToken1 = await accessTokenController.Create(TestInit1.ProjectId, new()
            {
                AccessTokenGroupId = TestInit1.AccessTokenGroupId_1,
                AccessTokenName = "tokenName1",
                Url = "https://foo.com/accessKey1",
                MaxTraffic = 11,
                MaxClient = 12,
                Lifetime = 13,
                EndTime = endTime1
            });
            Assert.AreNotEqual(0, accessToken1.SupportCode);
            Assert.AreEqual("tokenName1", accessToken1.AccessTokenName);
            Assert.AreEqual(TestInit1.AccessTokenGroupId_1, accessToken1.AccessTokenGroupId);
            Assert.IsNull(accessToken1.StartTime);
            Assert.AreEqual(endTime1, accessToken1.EndTime);
            Assert.AreEqual(11, accessToken1.MaxTraffic);
            Assert.AreEqual(12, accessToken1.MaxClient);
            Assert.AreEqual(13, accessToken1.Lifetime);
            Assert.AreEqual("https://foo.com/accessKey1", accessToken1.Url);

            var endTime2 = DateTime.Now.AddDays(2);
            var accessToken2A = await accessTokenController.Create(TestInit1.ProjectId, createParams: new()
            {
                AccessTokenGroupId = TestInit1.AccessTokenGroupId_2,
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
            Assert.AreEqual(TestInit1.AccessTokenGroupId_2, accessToken2A.AccessTokenGroupId);
            Assert.IsNull(accessToken2A.StartTime);
            Assert.AreEqual(endTime2, accessToken2A.EndTime);
            Assert.AreEqual(21, accessToken2A.MaxTraffic);
            Assert.AreEqual(22, accessToken2A.MaxClient);
            Assert.AreEqual(23, accessToken2A.Lifetime);
            Assert.AreEqual("https://foo.com/accessKey2", accessToken2A.Url);

            //-----------
            // check: get
            //-----------
            var accessToken2B = (await accessTokenController.Get(TestInit1.ProjectId, accessToken2A.AccessTokenId)).AccessToken;
            Assert.AreEqual(accessToken2A.EndTime?.ToString("dd-MM-yyyy hh:mm:ss"), accessToken2B.EndTime?.ToString("dd-MM-yyyy hh:mm:ss"));
            Assert.AreEqual(accessToken2A.AccessTokenId, accessToken2B.AccessTokenId);
            Assert.AreEqual(accessToken2A.AccessTokenGroupId, accessToken2B.AccessTokenGroupId);
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
                AccessTokenGroupId = accessToken2A.AccessTokenGroupId,
                EndTime = DateTime.Now.AddDays(4),
                Lifetime = 61,
                MaxClient = 7,
                MaxTraffic = 805004,
                Url = $"http:" + $"//www.sss.com/new{Guid.NewGuid()}.com",
            };

            await accessTokenController.Update(TestInit1.ProjectId, accessToken2A.AccessTokenId, updateParams: updateParams);
            accessToken2B = (await accessTokenController.Get(TestInit1.ProjectId, accessToken2A.AccessTokenId)).AccessToken;

            Assert.AreEqual(updateParams.EndTime.Value?.ToString("dd-MM-yyyy hh:mm:ss"), accessToken2B.EndTime?.ToString("dd-MM-yyyy hh:mm:ss"));
            Assert.AreEqual(accessToken2A.AccessTokenId, accessToken2B.AccessTokenId);
            Assert.AreEqual(updateParams.AccessTokenGroupId, accessToken2B.AccessTokenGroupId);
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
            var accessController = TestInit1.CreateAccessController();
            var certificateData = await accessController.GetSslCertificateData(TestInit1.ServerId_1, TestInit1.HostEndPoint_G2S1.ToString());
            var x509Certificate2 = new X509Certificate2(certificateData);

            var accessKey = await accessTokenController.GetAccessKey(TestInit1.ProjectId, accessTokenId: accessToken2B.AccessTokenId);
            var token = Token.FromAccessKey(accessKey.Key);
            Assert.AreEqual(x509Certificate2.GetNameInfo(X509NameType.DnsName, false), token.HostName);
            Assert.AreEqual(true, token.IsPublic);
            Assert.AreEqual(accessToken2B.AccessTokenName, token.Name);
            Assert.AreEqual(Convert.ToBase64String(x509Certificate2.GetCertHash()), Convert.ToBase64String(token.CertificateHash));
            Assert.AreEqual(Convert.ToBase64String(accessToken2B.Secret), Convert.ToBase64String(token.Secret));
            Assert.AreEqual(TestInit1.HostEndPoint_G2S1, token.HostEndPoint);
            Assert.AreEqual(accessToken2B.SupportCode, token.SupportId);
        }

        [TestMethod]
        public async Task GetAccessUsageLogs()
        {
            var accessController = TestInit1.CreateAccessController();
            var sessionRequestEx = TestInit1.CreateSessionRequestEx();

            var session = await accessController.Session_Create(TestInit1.ServerId_1, sessionRequestEx);

            // add usage
            var dateTime = DateTime.Now;
            UsageInfo usageInfo = new()
            {
                ReceivedTraffic = 1000 * 1000000,
                SentTraffic = 1000 * 1000000
            };
            await Task.Delay(500);
            await accessController.Session_AddUsage(TestInit1.ServerId_1, session.SessionId, closeSession: false, usageInfo: usageInfo);

            // get usage
            var accessTokenController = TestInit.CreateAccessTokenController();
            var usageLogs = await accessTokenController.GetAccessUsageLogs(TestInit1.ProjectId, accessTokenId: TestInit1.AccessTokenId_1, clientId: sessionRequestEx.ClientInfo.ClientId);
            var usageLog = usageLogs[0];
            Assert.IsNotNull(usageLog.Session);
            Assert.IsNotNull(usageLog.Session.Client);
            Assert.AreEqual(sessionRequestEx.ClientIp?.ToString(), usageLog.Session.ClientIp);
            Assert.AreEqual(sessionRequestEx.ClientInfo.ClientVersion, usageLog.Session.Client.ClientVersion); //make sure client is returned
            Assert.AreEqual(sessionRequestEx.ClientInfo.ClientId, usageLog.Session.Client.UserClientId);
            Assert.AreEqual(sessionRequestEx.ClientInfo.ClientVersion, usageLog.Session.ClientVersion);
            Assert.AreEqual(usageInfo.ReceivedTraffic, usageLog.ReceivedTraffic);
            Assert.AreEqual(usageInfo.SentTraffic, usageLog.SentTraffic);
            Assert.IsTrue(dateTime <= usageLog.CreatedTime);
        }

        [TestMethod]
        public async Task Create_Validate()
        {
            var accessTokenGroupController = TestInit.CreateAccessTokenGroupController();
            var project2_G1 = (await accessTokenGroupController.List(TestInit2.ProjectId))[0];

            // check create
            var accessTokenController = TestInit.CreateAccessTokenController();
            try
            {
                await accessTokenController.Create(TestInit1.ProjectId, createParams: new() { AccessTokenGroupId = project2_G1.AccessTokenGroup.AccessTokenGroupId });
                Assert.Fail("KeyNotFoundException is expected!");
            }
            catch (Exception ex) when (AccessUtil.IsNotExistsException(ex)) { }
        }

        [TestMethod]
        public async Task Update_Validate()
        {
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId, new() { });

            // check create
            try
            {
                await accessTokenController.Update(TestInit1.ProjectId, accessToken.AccessTokenId, new() { AccessTokenGroupId = TestInit2.AccessTokenGroupId_1 });
                Assert.Fail("KeyNotFoundException is expected!");
            }
            catch (Exception ex) when (AccessUtil.IsNotExistsException(ex)) { }
        }

        [TestMethod]
        public async Task List()
        {
            // create a new group with new server endpoint
            var accessTokenGroupController = TestInit.CreateAccessTokenGroupController();
            var accessTokenGroup = await accessTokenGroupController.Create(TestInit1.ProjectId, $"newGroup_{Guid.NewGuid()}");
            var hostEndPoint = await TestInit.NewEndPoint();

            await TestInit.CreateServerEndPointController().Create(TestInit1.ProjectId, hostEndPoint.ToString(),
                new() { AccessTokenGroupId = accessTokenGroup.AccessTokenGroupId });

            var accessTokenControl = TestInit.CreateAccessTokenController();
            var publicAccessToken = await accessTokenControl.Create(TestInit1.ProjectId, createParams: new() { AccessTokenGroupId = accessTokenGroup.AccessTokenGroupId, IsPublic = true });
            var privateAccessToken = await accessTokenControl.Create(TestInit1.ProjectId, createParams: new() { AccessTokenGroupId = accessTokenGroup.AccessTokenGroupId, IsPublic = false });

            // add usage
            var usageInfo = new UsageInfo { ReceivedTraffic = 10000000, SentTraffic = 10000000 };
            var accessController = TestInit1.CreateAccessController();
            var publicSessionResponseEx = await accessController.Session_Create(TestInit1.ServerId_1, TestInit1.CreateSessionRequestEx(publicAccessToken, hostEndPoint: hostEndPoint));
            await accessController.Session_AddUsage(TestInit1.ServerId_1, publicSessionResponseEx.SessionId, closeSession: false, usageInfo: usageInfo);

            var privateSessionResponseEx = await accessController.Session_Create(TestInit1.ServerId_1, TestInit1.CreateSessionRequestEx(privateAccessToken, hostEndPoint: hostEndPoint));
            await accessController.Session_AddUsage(TestInit1.ServerId_1, privateSessionResponseEx.SessionId, closeSession:false, usageInfo: usageInfo);

            // list
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessTokens = await accessTokenController.List(TestInit1.ProjectId, accessTokenGroupId: accessTokenGroup.AccessTokenGroupId);
            var publicItem = accessTokens.First(x => x.AccessToken.IsPublic);
            var privateItem = accessTokens.First(x => !x.AccessToken.IsPublic);
            Assert.IsNull(publicItem.AccessUsage);
            Assert.AreEqual(usageInfo.ReceivedTraffic, privateItem.AccessUsage?.CycleReceivedTraffic);
            Assert.AreEqual(usageInfo.SentTraffic, privateItem.AccessUsage?.CycleSentTraffic);

        }
    }
}
