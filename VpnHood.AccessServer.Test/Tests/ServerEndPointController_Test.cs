using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Transactions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Services;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class ServerEndPointController_Test
    {
        private TransactionScope _trans;

        [TestInitialize()]
        public void Init()
        {
            _trans = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        }

        [TestCleanup()]
        public void Cleanup()
        {
            _trans.Dispose();
        }

        [TestMethod]
        public async Task CRUD()
        {
            var dnsName = "Test_CRUD";
            var serverEndPointController = TestUtil.CreateServerEndPointController();
            var serverEndPointGroupId1 = 100;
            var serverEndPointId1 = TestInit.TEST_ServerEndPoint_New1.ToString();
            await serverEndPointController.Create(serverEndPoint: serverEndPointId1, subjectName: $"CN={dnsName}", serverEndPointGroupId: serverEndPointGroupId1, isDefault: true);

            //-----------
            // check: serverEndPointGroupId is created
            //-----------
            var serverEndPointGroupService = ServerEndPointGroupService.FromId(serverEndPointGroupId1);
            var serverEndPointGroup = await serverEndPointGroupService.Get();
            Assert.AreEqual(serverEndPointGroup.defaultServerEndPointId, serverEndPointId1);

            //-----------
            // check: serverEndPoint is created
            //-----------
            var serverEndPointService1 = ServerEndPointService.FromId(serverEndPointId1);
            var serverEndPoint = await serverEndPointService1.Get();
            Assert.AreEqual(serverEndPointGroupId1, serverEndPoint.serverEndPointGroupId);
            Assert.AreEqual(serverEndPointId1, serverEndPoint.serverEndPointId);

            //-----------
            // check: error for delete default endPoint
            //-----------
            try
            {
                await serverEndPointController.Delete(serverEndPointId1);
                Assert.Fail("Should not be able to delete default endPoint!");
            }
            catch (KeyNotFoundException) { }

            //-----------
            // check: delete no default endPoint
            //-----------

            // change default
            var serverEndPointId2 = TestInit.TEST_ServerEndPoint_New2.ToString();
            await serverEndPointController.Create(serverEndPoint: serverEndPointId2, subjectName: $"CN={dnsName}", serverEndPointGroupId: serverEndPointGroupId1, isDefault: true);
            serverEndPointGroup = await serverEndPointGroupService.Get();
            Assert.AreEqual(serverEndPointGroup.defaultServerEndPointId, serverEndPointId2);

            // create first one
            await serverEndPointController.Delete(serverEndPointId1);
            try
            {
                await serverEndPointService1.Get();
                Assert.Fail("EndPoint should not exist!");
            }
            catch (KeyNotFoundException) { }

            //-----------
            // check: create no default endPoint
            //-----------
            await serverEndPointController.Create(serverEndPoint: serverEndPointId1, subjectName: $"CN={dnsName}", serverEndPointGroupId: serverEndPointGroupId1, isDefault: false);
            serverEndPointGroup = await serverEndPointGroupService.Get();
            Assert.AreEqual(serverEndPointGroup.defaultServerEndPointId, serverEndPointId1);

            //-----------
            // check: first endPoint in group must be default even if isDefault is false
            //-----------
            await serverEndPointController.Create(serverEndPoint: TestInit.TEST_ServerEndPoint_G2S1.ToString(), subjectName: $"CN={dnsName}2", serverEndPointGroupId: TestInit.TEST_ServerEndPointGroup2, isDefault: false);
            var serverEndPointGroupService2 = ServerEndPointGroupService.FromId(TestInit.TEST_ServerEndPointGroup2);
            var serverEndPointGroup2 = await serverEndPointGroupService2.Get();
            Assert.AreEqual(serverEndPointGroup2.defaultServerEndPointId, TestInit.TEST_ServerEndPoint_G2S1.ToString());

        }

        [TestMethod]
        public async Task CreateFromCertificate()
        {
            var serverEndPointId1 = TestInit.TEST_ServerEndPoint_New1.ToString();
            var serverEndPointGroupId1 = 100;

            // create certificate raw data
            var certificate1 = CertificateUtil.CreateSelfSigned();
            var dnsName1 = certificate1.GetNameInfo(X509NameType.DnsName, false);
            var certificateRawData1 = certificate1.Export(X509ContentType.Pfx, "123");

            // create certificate
            var serverEndPointController = TestUtil.CreateServerEndPointController();
            await serverEndPointController.CreateFromCertificate(serverEndPoint: serverEndPointId1, serverEndPointGroupId: serverEndPointGroupId1, certificateRawData: certificateRawData1, password: "123", isDefault: false);

            //-----------
            // check: overwrite is false
            //-----------
            var certificate2 = CertificateUtil.CreateSelfSigned();
            var certificateRawData2 = certificate2.Export(X509ContentType.Pfx, "123");
            var dnsName2 = certificate2.GetNameInfo(X509NameType.DnsName, false);
            try
            {
                await serverEndPointController.CreateFromCertificate(serverEndPoint: serverEndPointId1, serverEndPointGroupId: serverEndPointGroupId1, certificateRawData: certificateRawData1, password: "123", isDefault: false);
                Assert.Fail("Exception Expect");
            }
            catch { }
            var serverEndPointService = ServerEndPointService.FromId(serverEndPointId1);
            var serverEndPoint = await serverEndPointService.Get();
            var certificate_t = new X509Certificate2(serverEndPoint.certificateRawData);
            Assert.AreEqual(dnsName1, certificate_t.GetNameInfo(X509NameType.DnsName, false));

            //-----------
            // check: overwrite is true
            //-----------
            await serverEndPointController.CreateFromCertificate(serverEndPoint: serverEndPointId1, serverEndPointGroupId: serverEndPointGroupId1, certificateRawData: certificateRawData2, password: "123", isDefault: false);
            serverEndPoint = await serverEndPointService.Get();
            certificate_t = new X509Certificate2(serverEndPoint.certificateRawData);
            Assert.AreEqual(dnsName2, certificate_t.GetNameInfo(X509NameType.DnsName, false));
        }

    }
}
