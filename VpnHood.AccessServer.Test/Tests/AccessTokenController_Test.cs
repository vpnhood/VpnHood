using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Data.SqlClient;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Transactions;
using VpnHood.Common;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class AccessTokenController_Test
    {
        private TransactionScope _trans;

        [TestInitialize()]
        public void Init()
        {
            _trans = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
            TestInit.InitCertificates().Wait();
        }

        [TestCleanup()]
        public void Cleanup()
        {
            _trans.Dispose();
        }

        [TestMethod]
        public async Task CRUD_public()
        {
            //-----------
            // check: create
            //-----------

            var accessTokenController = TestUtil.CreateAccessTokenController();
            var accessToken = await accessTokenController.CreatePublic(serverEndPoint: TestInit.TEST_PublicServerEndPoint, tokenName: "tokenName", maxTraffic: 10, tokenUrl: "https://foo.com/accessKey");
            Assert.AreNotEqual(0, accessToken.supportId);
            Assert.AreEqual("tokenName", accessToken.accessTokenName);
            Assert.AreEqual(TestInit.TEST_PublicServerEndPoint, accessToken.serverEndPoint);
            Assert.IsNull(accessToken.startTime);
            Assert.IsNull(accessToken.endTime);
            Assert.AreEqual(0, accessToken.lifetime);
            Assert.AreEqual(0, accessToken.maxClient);
            Assert.AreEqual(10, accessToken.maxTraffic);
            Assert.AreEqual("https://foo.com/accessKey", accessToken.url);
            Assert.IsTrue(accessToken.isPublic);

            //-----------
            // check: get
            //-----------
            var accessToken2 = await accessTokenController.GetAccessToken(accessToken.accessTokenId);
            Assert.AreEqual(JsonConvert.SerializeObject(accessToken), JsonConvert.SerializeObject(accessToken2));

            //-----------
            // check: getAccessKey
            //-----------
            var accessController = TestUtil.CreateAccessController();
            var certificateData = await accessController.GetSslCertificateData(accessToken.serverEndPoint);
            var x509Certificate2 = new X509Certificate2(certificateData);

            var accessKey = await accessTokenController.GetAccessKey(accessTokenId: accessToken.accessTokenId);
            var token = Token.FromAccessKey(accessKey);
            Assert.AreEqual(x509Certificate2.GetNameInfo(X509NameType.DnsName, false), token.DnsName);
            Assert.AreEqual(true, token.IsPublic);
            Assert.AreEqual(accessToken.accessTokenName, token.Name);
            Assert.AreEqual(Convert.ToBase64String(Token.ComputePublicKeyHash(x509Certificate2.GetPublicKey())), Convert.ToBase64String(token.PublicKeyHash));
            Assert.AreEqual(Convert.ToBase64String(accessToken.secret), Convert.ToBase64String(token.Secret));
            Assert.AreEqual(accessToken.serverEndPoint, token.ServerEndPoint);
            Assert.AreEqual(accessToken.supportId, token.SupportId);
        }

        [TestMethod]
        public async Task CRUD_private()
        {
            var accessTokenController = TestUtil.CreateAccessTokenController();

            var accessToken = await accessTokenController.CreatePrivate(serverEndPoint: TestInit.TEST_PrivateServerEndPoint,
                tokenName: "tokenName", maxTraffic: 10, maxClient: 5, endTime: new DateTime(2000, 1, 2), lifetime: 25, tokenUrl: "https://foo.com/accessKey2");
            Assert.AreNotEqual(0, accessToken.supportId);
            Assert.AreEqual("tokenName", accessToken.accessTokenName);
            Assert.AreEqual(TestInit.TEST_PrivateServerEndPoint, accessToken.serverEndPoint);
            Assert.IsNull(accessToken.startTime);
            Assert.AreEqual(new DateTime(2000, 1, 2), accessToken.endTime);
            Assert.AreEqual(25, accessToken.lifetime);
            Assert.AreEqual(5, accessToken.maxClient);
            Assert.AreEqual(10, accessToken.maxTraffic);
            Assert.IsFalse(accessToken.isPublic);
            Assert.AreEqual("https://foo.com/accessKey2", accessToken.url);

            //-----------
            // check: get
            //-----------
            var accessToken2 = await accessTokenController.GetAccessToken(accessToken.accessTokenId);
            Assert.AreEqual(JsonConvert.SerializeObject(accessToken), JsonConvert.SerializeObject(accessToken2));
        }

    }
}
