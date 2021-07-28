using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Transactions;
using VpnHood.AccessServer.Models;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class ServerEndPointController_Test
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
        public async Task CRUD()
        {
            var dnsName = "Test_CRUD";
            var serverEndPointController = TestHelper.CreateServerEndPointController();
            var publicEndPoint1 = TestInit.TEST_ServerEndPoint_New1.ToString();
            var serverEndPoint1 = await serverEndPointController.Create(TestInit.TEST_AccountId1, publicEndPoint: publicEndPoint1, 
                accessTokenGroupId: TestInit.TEST_AccessTokenGroup2, subjectName: $"CN={dnsName}", isDefault: true);

            //-----------
            // check: accessTokenGroupId is created
            //-----------
            var serverEndPoint1B = await serverEndPointController.Get(TestInit.TEST_AccountId1, publicEndPoint: publicEndPoint1);
            Assert.AreEqual(serverEndPoint1B.PulicEndPoint, serverEndPoint1.PulicEndPoint);
            Assert.IsTrue(serverEndPoint1B.IsDefault); // first group must be default

            //-----------
            // check: error for delete default serverEndPoint
            //-----------
            try
            {
                await serverEndPointController.Delete(TestInit.TEST_AccountId1, publicEndPoint1);
                Assert.Fail("Should not be able to delete default endPoint!");
            }
            catch (KeyNotFoundException) { }

            //-----------
            // check: delete no default endPoint
            //-----------

            // change default
            var publicEndPoint2 = TestInit.TEST_ServerEndPoint_New2;
            var serverEndPoint2 = await serverEndPointController.Create(TestInit.TEST_AccountId1, publicEndPoint: publicEndPoint2.ToString(), accessTokenGroupId: TestInit.TEST_AccessTokenGroup2, subjectName: $"CN={dnsName}", isDefault: true);
            serverEndPoint1 = await serverEndPointController.Get(TestInit.TEST_AccountId1, publicEndPoint: publicEndPoint1);

            Assert.IsTrue(serverEndPoint2.IsDefault, "ServerEndPoint2 must be default");
            Assert.IsFalse(serverEndPoint1.IsDefault, "ServerEndPoint1 should not be default");

            // create first one
            await serverEndPointController.Delete(TestInit.TEST_AccountId1, publicEndPoint1);
            try
            {
                await serverEndPointController.Get(TestInit.TEST_AccountId1, publicEndPoint1);
                Assert.Fail("EndPoint should not exist!");
            }
            catch (KeyNotFoundException) { }

            //-----------
            // check: create no default endPoint
            //-----------
            await serverEndPointController.Create(TestInit.TEST_AccountId1, publicEndPoint: publicEndPoint1, subjectName: $"CN={dnsName}", accessTokenGroupId: TestInit.TEST_AccessTokenGroup2, isDefault: false);
            serverEndPoint1 = await serverEndPointController.Get(TestInit.TEST_AccountId1, publicEndPoint1);
            Assert.IsFalse(serverEndPoint1.IsDefault, "ServerEndPoint1 should not be default");
            Assert.IsTrue(serverEndPoint2.IsDefault, "ServerEndPoint2 must be default");

            //-----------
            // check: first endPoint in group must be default even if isDefault is false
            //-----------
            var accessTokenGroupController = TestHelper.CreateAccessTokenGroupController();
            var accessTokenGroup = await accessTokenGroupController.Create(TestInit.TEST_AccountId1, $"NewGroup-{Guid.NewGuid()}");
            var publicEndPoint3 = TestInit.TEST_ServerEndPoint_New3;
            await serverEndPointController.Create(TestInit.TEST_AccountId1, publicEndPoint: TestInit.TEST_ServerEndPoint_G2S1.ToString(), subjectName: $"CN={dnsName}2", accessTokenGroupId: accessTokenGroup.AccessTokenGroupId, isDefault: false);
            var serverEndPoint3 = await serverEndPointController.Get(TestInit.TEST_AccountId1, publicEndPoint3.ToString());
            Assert.IsTrue(serverEndPoint3.IsDefault);
        }

        [TestMethod]
        public async Task CreateFromCertificate()
        {
            var publicEndPoint1 = TestInit.TEST_ServerEndPoint_New1.ToString();
            var accessTokenGroupId1 = TestInit.TEST_AccessTokenGroup1;

            // create certificate raw data
            var certificate1 = CertificateUtil.CreateSelfSigned();
            var dnsName1 = certificate1.GetNameInfo(X509NameType.DnsName, false);
            var certificateRawData1 = certificate1.Export(X509ContentType.Pfx, "123");

            // create certificate
            var serverEndPointController = TestHelper.CreateServerEndPointController();
            var serverEndPoint1A = await serverEndPointController.CreateFromCertificate(TestInit.TEST_AccountId1, publicEndPoint: publicEndPoint1,
                accessTokenGroupId: accessTokenGroupId1, certificateRawData: certificateRawData1, password: "123", isDefault: false);

            //-----------
            // check: overwrite is false
            //-----------
            var certificate2 = CertificateUtil.CreateSelfSigned();
            var certificateRawData2 = certificate2.Export(X509ContentType.Pfx, "123");
            var dnsName2 = certificate2.GetNameInfo(X509NameType.DnsName, false);
            try
            {
                await serverEndPointController.CreateFromCertificate(TestInit.TEST_AccountId1, publicEndPoint: publicEndPoint1, accessTokenGroupId: accessTokenGroupId1, certificateRawData: certificateRawData1, password: "123", isDefault: false);
                Assert.Fail("Exception Expect");
            }
            catch (Exception ex) when (AccessUtil.IsAlreadyExistsException(ex))
            { }

            var serverEndPoint1B = await serverEndPointController.Get(TestInit.TEST_AccountId1, serverEndPoint1A.PulicEndPoint);
            var certificate_t = new X509Certificate2(serverEndPoint1B.CertificateRawData);
            Assert.AreEqual(serverEndPoint1A.PulicEndPoint, serverEndPoint1B.PulicEndPoint);
            Assert.AreEqual(dnsName1, certificate_t.GetNameInfo(X509NameType.DnsName, false));

            //-----------
            // check: overwrite is true
            //-----------
            await serverEndPointController.CreateFromCertificate(TestInit.TEST_AccountId1, publicEndPoint: publicEndPoint1, accessTokenGroupId: accessTokenGroupId1, certificateRawData: certificateRawData1, password: "123", isDefault: false);
            await serverEndPointController.CreateFromCertificate(TestInit.TEST_AccountId1, publicEndPoint: publicEndPoint1, accessTokenGroupId: accessTokenGroupId1, certificateRawData: certificateRawData2, password: "123", isDefault: false);
            serverEndPoint1A = await serverEndPointController.Get(TestInit.TEST_AccountId1, publicEndPoint1);
            certificate_t = new X509Certificate2(serverEndPoint1A.CertificateRawData);
            Assert.AreEqual(dnsName2, certificate_t.GetNameInfo(X509NameType.DnsName, false));
        }

    }
}
