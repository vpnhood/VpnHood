using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;
using System.Transactions;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class AccessTokenController_Test
    {
        private TransactionScope _trans;

        [TestInitialize()]
        public async Task Init()
        {
            _trans = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
            await TestInit.InitCertificates();
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
            var accessToken = await accessTokenController.CreatePublic(serverEndPoint: TestInit.TEST_PublicServerEndPoint, tokenName: "tokenName", maxTraffic: 10);
            Assert.AreNotEqual(0, accessToken.supportId);
            Assert.AreEqual("tokenName", accessToken.accessTokenName);
            Assert.AreEqual(TestInit.TEST_PublicServerEndPoint, accessToken.serverEndPoint);
            Assert.IsNull(accessToken.startTime);
            Assert.IsNull(accessToken.endTime);
            Assert.AreEqual(0, accessToken.lifetime);
            Assert.AreEqual(0, accessToken.maxClient);
            Assert.AreEqual(10, accessToken.maxTraffic);
            Assert.IsTrue(accessToken.isPublic);

            //-----------
            // check: get
            //-----------
            var accessToken2 = await accessTokenController.Get(accessToken.accessTokenId);
            Assert.AreEqual(JsonConvert.SerializeObject(accessToken), JsonConvert.SerializeObject(accessToken2));
        }

        [TestMethod]
        public async Task CRUD_private()
        {
            var accessTokenController = TestUtil.CreateAccessTokenController();

            var accessToken = await accessTokenController.CreatePrivate(serverEndPoint: TestInit.TEST_PrivateServerEndPoint,
                tokenName: "tokenName", maxTraffic: 10, maxClient: 5, endTime: new DateTime(2000, 1, 2), lifetime: 25);
            Assert.AreNotEqual(0, accessToken.supportId);
            Assert.AreEqual("tokenName", accessToken.accessTokenName);
            Assert.AreEqual(TestInit.TEST_PrivateServerEndPoint, accessToken.serverEndPoint);
            Assert.IsNull(accessToken.startTime);
            Assert.AreEqual(new DateTime(2000, 1, 2), accessToken.endTime);
            Assert.AreEqual(25, accessToken.lifetime);
            Assert.AreEqual(5, accessToken.maxClient);
            Assert.AreEqual(10, accessToken.maxTraffic);
            Assert.IsFalse(accessToken.isPublic);

            //-----------
            // check: get
            //-----------
            var accessToken2 = await accessTokenController.Get(accessToken.accessTokenId);
            Assert.AreEqual(JsonConvert.SerializeObject(accessToken), JsonConvert.SerializeObject(accessToken2));
        }

    }
}
