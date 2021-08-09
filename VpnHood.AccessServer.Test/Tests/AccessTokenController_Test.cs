using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;
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
            var accessToken1_1 = await accessTokenController.Create(TestInit1.ProjectId);
            var accessToken2_1 = await accessTokenController.Create(TestInit2.ProjectId);
            var accessToken1_2 = await accessTokenController.Create(TestInit1.ProjectId);
            var accessToken2_2 = await accessTokenController.Create(TestInit2.ProjectId);

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

            var accessToken1 = await accessTokenController.Create(TestInit1.ProjectId, accessTokenGroupId: TestInit1.AccessTokenGroupId_1,
                tokenName: "tokenName1", tokenUrl: "https://foo.com/accessKey1", maxTraffic: 11, maxClient: 12, lifetime: 13, endTime: endTime1);
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
            var accessToken2A = await accessTokenController.Create(TestInit1.ProjectId, accessTokenGroupId: TestInit1.AccessTokenGroupId_2,
                tokenName: "tokenName2", tokenUrl: "https://foo.com/accessKey2", maxTraffic: 21, maxClient: 22, lifetime: 23, endTime: endTime2, isPublic: true);
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
            var accessToken2B = await accessTokenController.Get(TestInit1.ProjectId, accessToken2A.AccessTokenId);
            Assert.AreEqual(accessToken2A.EndTime.Value.ToString("dd-MM-yyyy hh:mm:ss"), accessToken2B.EndTime.Value.ToString("dd-MM-yyyy hh:mm:ss"));
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
            accessToken2A.AccessTokenGroupId = TestInit1.AccessTokenGroupId_2;
            accessToken2A.AccessTokenName = $"new_name_{Guid.NewGuid()}";
            accessToken2A.EndTime = DateTime.Now.AddDays(4);
            accessToken2A.Lifetime = 61;
            accessToken2A.MaxClient = 7;
            accessToken2A.MaxTraffic = 805004;
            accessToken2A.Url = $"http:" + $"//www.sss.com/new{Guid.NewGuid()}.com";

            await accessTokenController.Update(TestInit1.ProjectId, accessToken2A.AccessTokenId, accessToken2A);
            accessToken2B = await accessTokenController.Get(TestInit1.ProjectId, accessToken2A.AccessTokenId);

            Assert.AreEqual(accessToken2A.EndTime.Value.ToString("dd-MM-yyyy hh:mm:ss"), accessToken2B.EndTime.Value.ToString("dd-MM-yyyy hh:mm:ss"));
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

            //-----------
            // check: getAccessKey
            //-----------
            var accessController = TestInit1.CreateAccessController();
            var certificateData = await accessController.GetSslCertificateData(TestInit1.ServerId_1, TestInit1.ServerEndPoint_G2S1.ToString());
            var x509Certificate2 = new X509Certificate2(certificateData);

            var accessKey = await accessTokenController.GetAccessKey(TestInit1.ProjectId, accessTokenId: accessToken2A.AccessTokenId);
            var token = Token.FromAccessKey(accessKey.Key);
            Assert.AreEqual(x509Certificate2.GetNameInfo(X509NameType.DnsName, false), token.DnsName);
            Assert.AreEqual(true, token.IsPublic);
            Assert.AreEqual(accessToken2A.AccessTokenName, token.Name);
            Assert.AreEqual(Convert.ToBase64String(x509Certificate2.GetCertHash()), Convert.ToBase64String(token.CertificateHash));
            Assert.AreEqual(Convert.ToBase64String(accessToken2A.Secret), Convert.ToBase64String(token.Secret));
            Assert.AreEqual(TestInit1.ServerEndPoint_G2S1, token.ServerEndPoint);
            Assert.AreEqual(accessToken2A.SupportCode, token.SupportId);
        }

        [TestMethod]
        public async Task GetAccessUsageLogs()
        {
            var accessController = TestInit1.CreateAccessController();
            var accessRequest = TestInit1.CreateAccessRequest();

            var access = await accessController.Get(TestInit1.ServerId_1, accessRequest);

            // add usage
            var dateTime = DateTime.Now;
            UsageInfo usageInfo = new()
            {
                ReceivedTrafficByteCount = 1000 * 1000000,
                SentTrafficByteCount = 1000 * 1000000
            };
            await Task.Delay(500);
            await accessController.AddUsage(TestInit1.ServerId_1, access.AccessId, usageInfo);

            // get usage
            var accessTokenController = TestInit.CreateAccessTokenController();
            var usageLog = await accessTokenController.GetAccessUsageLogs(TestInit1.ProjectId, accessTokenId: TestInit1.AccessTokenId_1, clientId: accessRequest.ClientInfo.ClientId);
            Assert.AreEqual(accessRequest.ClientInfo.ClientIp.ToString(), usageLog[0].ClientIp);
            Assert.AreEqual(accessRequest.ClientInfo.ClientVersion, usageLog[0].Client.ClientVersion); //make sure client is returned
            Assert.AreEqual(accessRequest.ClientInfo.ClientId, usageLog[0].Client.ClientId);
            Assert.AreEqual(accessRequest.ClientInfo.ClientVersion, usageLog[0].ClientVersion);
            Assert.AreEqual(usageInfo.ReceivedTrafficByteCount, usageLog[0].ReceivedTraffic);
            Assert.AreEqual(usageInfo.SentTrafficByteCount, usageLog[0].SentTraffic);
            Assert.IsTrue(dateTime <= usageLog[0].CreatedTime);
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
                await accessTokenController.Create(TestInit1.ProjectId, accessTokenGroupId: project2_G1.AccessTokenGroup.AccessTokenGroupId);
                Assert.Fail("KeyNotFoundException is expected!");
            }
            catch (Exception ex) when (AccessUtil.IsNotExistsException(ex)) { }
        }

        [TestMethod]
        public async Task Update_Validate()
        {
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessToken = await accessTokenController.Create(TestInit1.ProjectId);

            // check create
            try
            {
                accessToken.AccessTokenGroupId = TestInit2.AccessTokenGroupId_1;
                await accessTokenController.Update(TestInit1.ProjectId, accessToken.AccessTokenId, accessToken);
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
            var serverEndPoint = await TestInit.NewEndPoint();

            await TestInit.CreateServerEndPointController().Create(TestInit1.ProjectId, serverEndPoint.ToString(), accessTokenGroup.AccessTokenGroupId);


            var accessTokenControl = TestInit.CreateAccessTokenController();
            var publicAccessToken = await accessTokenControl.Create(TestInit1.ProjectId, accessTokenGroupId: accessTokenGroup.AccessTokenGroupId, isPublic: true);
            var privateAccessToken = await accessTokenControl.Create(TestInit1.ProjectId, accessTokenGroupId: accessTokenGroup.AccessTokenGroupId, isPublic: false);

            // add usage
            var usageInfo = new UsageInfo { ReceivedTrafficByteCount = 10000000, SentTrafficByteCount = 10000000 };
            var accessController = TestInit1.CreateAccessController();
            var publicAccess = await accessController.Get(TestInit1.ServerId_1, TestInit1.CreateAccessRequest(publicAccessToken.AccessTokenId, requestEndPoint: serverEndPoint));
            await accessController.AddUsage(TestInit1.ServerId_1, publicAccess.AccessId, usageInfo);

            var privateAccess = await accessController.Get(TestInit1.ServerId_1, TestInit1.CreateAccessRequest(privateAccessToken.AccessTokenId, requestEndPoint: serverEndPoint));
            await accessController.AddUsage(TestInit1.ServerId_1, privateAccess.AccessId, usageInfo);

            // list
            var accessTokenController = TestInit.CreateAccessTokenController();
            var accessTokens = await accessTokenController.List(TestInit1.ProjectId, accessTokenGroupId: accessTokenGroup.AccessTokenGroupId);
            var publicItem = accessTokens.First(x => x.AccessToken.IsPublic);
            var privateItem = accessTokens.First(x => !x.AccessToken.IsPublic);
            Assert.IsNull(publicItem.AccessUsage);
            Assert.AreEqual(usageInfo.ReceivedTrafficByteCount, privateItem.AccessUsage.CycleReceivedTraffic);
            Assert.AreEqual(usageInfo.SentTrafficByteCount, privateItem.AccessUsage.CycleSentTraffic);

        }
    }
}
