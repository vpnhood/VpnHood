using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using VpnHood.Common;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class AccessTokenController_Test : ControllerTest
    {
        [TestMethod]
        public async Task CRUD_public()
        {
            //-----------
            // check: create
            //-----------
            var accessTokenController = TestInit.CreateAccessTokenController();

            var endTime1 = DateTime.Today.AddDays(1);
            endTime1 = endTime1.AddMilliseconds(-endTime1.Millisecond);

            var accessToken1 = await accessTokenController.Create(TestInit.AccountId_1, accessTokenGroupId: TestInit.AccessTokenGroupId_1,
                tokenName: "tokenName1", tokenUrl: "https://foo.com/accessKey1", maxTraffic: 11, maxClient: 12, lifetime: 13, endTime: endTime1);
            Assert.AreNotEqual(0, accessToken1.SupportCode);
            Assert.AreEqual("tokenName1", accessToken1.AccessTokenName);
            Assert.AreEqual(TestInit.AccessTokenGroupId_1, accessToken1.AccessTokenGroupId);
            Assert.IsNull(accessToken1.StartTime);
            Assert.AreEqual(endTime1, accessToken1.EndTime);
            Assert.AreEqual(11, accessToken1.MaxTraffic);
            Assert.AreEqual(12, accessToken1.MaxClient);
            Assert.AreEqual(13, accessToken1.Lifetime);
            Assert.AreEqual("https://foo.com/accessKey1", accessToken1.Url);

            var endTime2 = DateTime.Now.AddDays(2);
            var accessToken2A = await accessTokenController.Create(TestInit.AccountId_1, accessTokenGroupId: TestInit.AccessTokenGroupId_2,
                tokenName: "tokenName2", tokenUrl: "https://foo.com/accessKey2", maxTraffic: 21, maxClient: 22, lifetime: 23, endTime: endTime2, isPublic: true);
            Assert.AreNotEqual(0, accessToken2A.SupportCode);
            Assert.AreEqual("tokenName2", accessToken2A.AccessTokenName);
            Assert.AreEqual(TestInit.AccessTokenGroupId_2, accessToken2A.AccessTokenGroupId);
            Assert.IsNull(accessToken2A.StartTime);
            Assert.AreEqual(endTime2, accessToken2A.EndTime);
            Assert.AreEqual(21, accessToken2A.MaxTraffic);
            Assert.AreEqual(22, accessToken2A.MaxClient);
            Assert.AreEqual(23, accessToken2A.Lifetime);
            Assert.AreEqual("https://foo.com/accessKey2", accessToken2A.Url);

            //-----------
            // check: get
            //-----------
            var accessToken2B = await accessTokenController.GetAccessToken(TestInit.AccountId_1, accessToken2A.AccessTokenId);
            Assert.AreEqual(accessToken2A.EndTime.Value.ToString("dd-MM-yyyy hh:mm:ss"), accessToken2B.EndTime.Value.ToString("dd-MM-yyyy hh:mm:ss"));
            accessToken2A.EndTime = accessToken2B.EndTime;
            Assert.AreEqual(JsonConvert.SerializeObject(accessToken2A), JsonConvert.SerializeObject(accessToken2B));


            //-----------
            // check: getAccessKey
            //-----------
            var accessController = TestInit.CreateAccessController();
            var certificateData = await accessController.GetSslCertificateData(TestInit.ServerId_1, TestInit.ServerEndPoint_G2S1.ToString());
            var x509Certificate2 = new X509Certificate2(certificateData);

            var accessKey = await accessTokenController.GetAccessKey(TestInit.AccountId_1, accessTokenId: accessToken2A.AccessTokenId);
            var token = Token.FromAccessKey(accessKey);
            Assert.AreEqual(x509Certificate2.GetNameInfo(X509NameType.DnsName, false), token.DnsName);
            Assert.AreEqual(true, token.IsPublic);
            Assert.AreEqual(accessToken2A.AccessTokenName, token.Name);
            Assert.AreEqual(Convert.ToBase64String(x509Certificate2.GetCertHash()), Convert.ToBase64String(token.CertificateHash));
            Assert.AreEqual(Convert.ToBase64String(accessToken2A.Secret), Convert.ToBase64String(token.Secret));
            Assert.AreEqual(TestInit.ServerEndPoint_G2S1, token.ServerEndPoint);
            Assert.AreEqual(accessToken2A.SupportCode, token.SupportId);
        }

        [TestMethod]
        public async Task GetAccessUsageLogs()
        {
            var accessController = TestInit.CreateAccessController();
            ClientIdentity clientIdentity = new() { 
                TokenId = TestInit.AccessTokenId_1,
                ClientId = Guid.NewGuid(),
                ClientIp = await TestInit.NewIp(),
                ClientVersion = "1.1.1"

            };
            
            var access = await accessController.GetAccess(TestInit.ServerId_1, new() {
                ClientIdentity = clientIdentity,
                RequestEndPoint = TestInit.ServerEndPoint_G1S1
            });

            // add usage
            var dateTime = DateTime.Now;
            UsageParams usageParams = new()
            {
                AccessId = access.AccessId,
                ClientIdentity = clientIdentity,
                ReceivedTrafficByteCount = 1000 * 1000000,
                SentTrafficByteCount = 1000 * 1000000
            };
            await accessController.AddUsage(TestInit.ServerId_1, usageParams);

            // get usage
            var accessTokenController = TestInit.CreateAccessTokenController();
            var usageLog = await accessTokenController.GetAccessUsageLogs(TestInit.AccountId_1, accessTokenId: TestInit.AccessTokenId_1, clientId: clientIdentity.ClientId);
            Assert.AreEqual(usageParams.ClientIdentity.ClientIp.ToString(), usageLog[0].ClientIp);
            Assert.AreEqual(usageParams.ClientIdentity.ClientVersion, usageLog[0].Client.ClientVersion); //make sure client is returned
            Assert.AreEqual(usageParams.ClientIdentity.ClientId, usageLog[0].Client.ClientId);
            Assert.AreEqual(usageParams.ClientIdentity.ClientVersion, usageLog[0].ClientVersion);
            Assert.AreEqual(usageParams.ReceivedTrafficByteCount, usageLog[0].ReceivedTraffic);
            Assert.AreEqual(usageParams.SentTrafficByteCount, usageLog[0].SentTraffic);
            Assert.IsTrue(dateTime <= usageLog[0].CreatedTime);
        }
    }
}
