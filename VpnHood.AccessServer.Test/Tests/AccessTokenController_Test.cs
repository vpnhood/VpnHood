using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using VpnHood.Common;

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

            var accessToken1 = await accessTokenController.Create(TestInit.AccountId1, accessTokenGroupId: TestInit.AccessTokenGroup1,
                tokenName: "tokenName1", tokenUrl: "https://foo.com/accessKey1", maxTraffic: 11, maxClient: 12, lifetime: 13, endTime: endTime1);
            Assert.AreNotEqual(0, accessToken1.SupportCode);
            Assert.AreEqual("tokenName1", accessToken1.AccessTokenName);
            Assert.AreEqual(TestInit.AccessTokenGroup1, accessToken1.AccessTokenGroupId);
            Assert.IsNull(accessToken1.StartTime);
            Assert.AreEqual(endTime1, accessToken1.EndTime);
            Assert.AreEqual(11, accessToken1.MaxTraffic);
            Assert.AreEqual(12, accessToken1.MaxClient);
            Assert.AreEqual(13, accessToken1.Lifetime);
            Assert.AreEqual("https://foo.com/accessKey1", accessToken1.Url);

            var endTime2 = DateTime.Now.AddDays(2);
            var accessToken2A = await accessTokenController.Create(TestInit.AccountId1, accessTokenGroupId: TestInit.AccessTokenGroup2,
                tokenName: "tokenName2", tokenUrl: "https://foo.com/accessKey2", maxTraffic: 21, maxClient: 22, lifetime: 23, endTime: endTime2, isPublic: true);
            Assert.AreNotEqual(0, accessToken2A.SupportCode);
            Assert.AreEqual("tokenName2", accessToken2A.AccessTokenName);
            Assert.AreEqual(TestInit.AccessTokenGroup2, accessToken2A.AccessTokenGroupId);
            Assert.IsNull(accessToken2A.StartTime);
            Assert.AreEqual(endTime2, accessToken2A.EndTime);
            Assert.AreEqual(21, accessToken2A.MaxTraffic);
            Assert.AreEqual(22, accessToken2A.MaxClient);
            Assert.AreEqual(23, accessToken2A.Lifetime);
            Assert.AreEqual("https://foo.com/accessKey2", accessToken2A.Url);

            //-----------
            // check: get
            //-----------
            var accessToken2B = await accessTokenController.GetAccessToken(accessToken2A.AccessTokenId);
            Assert.AreEqual(accessToken2A.EndTime.Value.ToString("dd-MM-yyyy hh:mm:ss"), accessToken2B.EndTime.Value.ToString("dd-MM-yyyy hh:mm:ss"));
            accessToken2A.EndTime = accessToken2B.EndTime;
            Assert.AreEqual(JsonConvert.SerializeObject(accessToken2A), JsonConvert.SerializeObject(accessToken2B));


            //-----------
            // check: getAccessKey
            //-----------
            var accessController = TestInit.CreateAccessController();
            var certificateData = await accessController.GetSslCertificateData(TestInit.ServerId_1, TestInit.ServerEndPoint_G2S1.ToString());
            var x509Certificate2 = new X509Certificate2(certificateData);

            var accessKey = await accessTokenController.GetAccessKey(TestInit.AccountId1, accessTokenId: accessToken2A.AccessTokenId);
            var token = Token.FromAccessKey(accessKey);
            Assert.AreEqual(x509Certificate2.GetNameInfo(X509NameType.DnsName, false), token.DnsName);
            Assert.AreEqual(true, token.IsPublic);
            Assert.AreEqual(accessToken2A.AccessTokenName, token.Name);
            Assert.AreEqual(Convert.ToBase64String(x509Certificate2.GetCertHash()), Convert.ToBase64String(token.CertificateHash));
            Assert.AreEqual(Convert.ToBase64String(accessToken2A.Secret), Convert.ToBase64String(token.Secret));
            Assert.AreEqual(TestInit.ServerEndPoint_G2S1, token.ServerEndPoint);
            Assert.AreEqual(accessToken2A.SupportCode, token.SupportId);
        }
    }
}
