using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Transactions;
using VpnHood.AccessServer.Models;
using VpnHood.Common;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class AccessTokenController_Test
    {
        private TransactionScope _trans;
        private VhContext _vhContext;


        [TestInitialize()]
        public void Init()
        {
            _trans = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
            _vhContext = new();
            TestInit.Init().Wait();
        }

        [TestCleanup()]
        public void Cleanup()
        {
            _vhContext.Dispose();
            _trans.Dispose();
        }

        [TestMethod]
        public async Task CRUD_public()
        {
            //-----------
            // check: create
            //-----------

            var accessTokenController = TestHelper.CreateAccessTokenController();

            var endTime1 = DateTime.Now.AddDays(1);
            var accessToken1 = await accessTokenController.Create(serverEndPointGroupId: TestInit.TEST_ServerEndPointGroup1, 
                tokenName: "tokenName1", tokenUrl: "https://foo.com/accessKey1" , maxTraffic: 11, maxClient: 12, lifetime: 13, endTime : endTime1);
            Assert.AreNotEqual(0, accessToken1.SupportId);
            Assert.AreEqual("tokenName1", accessToken1.AccessTokenName);
            Assert.AreEqual(TestInit.TEST_ServerEndPointGroup1, accessToken1.ServerEndPointGroupId);
            Assert.IsNull(accessToken1.StartTime);
            Assert.AreEqual(endTime1, accessToken1.EndTime);
            Assert.AreEqual(11, accessToken1.MaxTraffic);
            Assert.AreEqual(12, accessToken1.MaxClient);
            Assert.AreEqual(13, accessToken1.Lifetime);
            Assert.AreEqual("https://foo.com/accessKey1", accessToken1.Url);


            var endTime2 = DateTime.Now.AddDays(2);
            var accessToken2 = await accessTokenController.Create(serverEndPointGroupId: TestInit.TEST_ServerEndPointGroup2, 
                tokenName: "tokenName2", tokenUrl: "https://foo.com/accessKey2", maxTraffic: 21, maxClient: 22, lifetime: 23, endTime: endTime2);
            Assert.AreNotEqual(0, accessToken2.SupportId);
            Assert.AreEqual("tokenName2", accessToken2.AccessTokenName);
            Assert.AreEqual(TestInit.TEST_ServerEndPointGroup2, accessToken2.ServerEndPointGroupId);
            Assert.IsNull(accessToken2.StartTime);
            Assert.AreEqual(endTime2, accessToken2.EndTime);
            Assert.AreEqual(21, accessToken1.MaxTraffic);
            Assert.AreEqual(22, accessToken1.MaxClient);
            Assert.AreEqual(23, accessToken1.Lifetime);
            Assert.AreEqual("https://foo.com/accessKey1", accessToken1.Url);

            //-----------
            // check: get
            //-----------
            var accessToken2_t = await accessTokenController.GetAccessToken(accessToken2.AccessTokenId);
            Assert.AreEqual(JsonConvert.SerializeObject(accessToken2), JsonConvert.SerializeObject(accessToken2_t));

            //-----------
            // check: getAccessKey
            //-----------
            var accessController = TestHelper.CreateAccessController();
            var certificateData = await accessController.GetSslCertificateData(TestInit.TEST_ServerEndPoint_G2S1.ToString());
            var x509Certificate2 = new X509Certificate2(certificateData);

            var accessKey = await accessTokenController.GetAccessKey(accessTokenId: accessToken2.AccessTokenId);
            var token = Token.FromAccessKey(accessKey);
            Assert.AreEqual(x509Certificate2.GetNameInfo(X509NameType.DnsName, false), token.DnsName);
            Assert.AreEqual(true, token.IsPublic);
            Assert.AreEqual(accessToken2.AccessTokenName, token.Name);
            Assert.AreEqual(Convert.ToBase64String(x509Certificate2.GetCertHash()), Convert.ToBase64String(token.CertificateHash));
            Assert.AreEqual(Convert.ToBase64String(accessToken2.Secret), Convert.ToBase64String(token.Secret));
            Assert.AreEqual(TestInit.TEST_ServerEndPoint_G2S1, token.ServerEndPoint.ToString());
            Assert.AreEqual(accessToken2.SupportId, token.SupportId);
        }
    }
}
