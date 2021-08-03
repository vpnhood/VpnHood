using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class ServerEndPointController_Test : ControllerTest
    {

        [TestMethod]
        public async Task CRUD()
        {
            var dnsName = "Test_CRUD";
            var serverEndPointController = TestInit.CreateServerEndPointController();
            var publicEndPoint1 = TestInit1.ServerEndPoint_New1.ToString();
            var serverEndPoint1 = await serverEndPointController.Create(TestInit1.ProjectId, publicEndPoint: publicEndPoint1, 
                accessTokenGroupId: TestInit1.AccessTokenGroupId_2, subjectName: $"CN={dnsName}", makeDefault: true);

            //-----------
            // check: accessTokenGroupId is created
            //-----------
            var serverEndPoint1B = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint: publicEndPoint1);
            Assert.AreEqual(serverEndPoint1B.PulicEndPoint, serverEndPoint1.PulicEndPoint);
            Assert.IsTrue(serverEndPoint1B.IsDefault); // first group must be default

            //-----------
            // check: error for delete default serverEndPoint
            //-----------
            try
            {
                await serverEndPointController.Delete(TestInit1.ProjectId, publicEndPoint1);
                Assert.Fail("Should not be able to delete default endPoint!");
            }
            catch (InvalidOperationException) { }

            //-----------
            // check: delete no default endPoint
            //-----------

            // change default
            var publicEndPoint2 = TestInit1.ServerEndPoint_New2;
            var serverEndPoint2 = await serverEndPointController.Create(TestInit1.ProjectId, publicEndPoint: publicEndPoint2.ToString(), accessTokenGroupId: TestInit1.AccessTokenGroupId_2, subjectName: $"CN={dnsName}", makeDefault: true);
            serverEndPoint1 = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint: publicEndPoint1);

            Assert.IsTrue(serverEndPoint2.IsDefault, "ServerEndPoint2 must be default");
            Assert.IsFalse(serverEndPoint1.IsDefault, "ServerEndPoint1 should not be default");

            // delete
            await serverEndPointController.Delete(TestInit1.ProjectId, publicEndPoint1);
            try
            {
                await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint1);
                Assert.Fail("EndPoint should not exist!");
            }
            catch (Exception ex) when (AccessUtil.IsNotExistsException(ex)) { }

            //-----------
            // check: create no default endPoint
            //-----------
            await serverEndPointController.Create(TestInit1.ProjectId, publicEndPoint: publicEndPoint1, subjectName: $"CN={dnsName}", accessTokenGroupId: TestInit1.AccessTokenGroupId_2, makeDefault: false);
            serverEndPoint1 = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint1);
            Assert.IsFalse(serverEndPoint1.IsDefault, "ServerEndPoint1 should not be default");
            Assert.IsTrue(serverEndPoint2.IsDefault, "ServerEndPoint2 must be default");

            //-----------
            // check: first created endPoint in group must be default even if isDefault is false
            //-----------
            var accessTokenGroupController = TestInit.CreateAccessTokenGroupController();
            var accessTokenGroup = await accessTokenGroupController.Create(TestInit1.ProjectId, $"NewGroup-{Guid.NewGuid()}");
            var publicEndPoint3 = TestInit1.ServerEndPoint_New3;
            await serverEndPointController.Create(TestInit1.ProjectId, publicEndPoint: publicEndPoint3.ToString(), 
                subjectName: $"CN={dnsName}2", accessTokenGroupId: accessTokenGroup.AccessTokenGroupId, makeDefault: false);
            var serverEndPoint3 = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint3.ToString());
            Assert.IsTrue(serverEndPoint3.IsDefault);

            //-----------
            // check: update with new certificate
            //-----------
            var certificate2 = CertificateUtil.CreateSelfSigned();
            var certificateRawData2 = certificate2.Export(X509ContentType.Pfx, "123");
            var dnsName2 = certificate2.GetNameInfo(X509NameType.DnsName, false);
            await serverEndPointController.Update(TestInit1.ProjectId, publicEndPoint: publicEndPoint1, accessTokenGroupId: TestInit1.AccessTokenGroupId_2, certificateRawData2, password: "123", makeDefault: true);
            serverEndPoint1B = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint1);
            var certificate_t = new X509Certificate2(serverEndPoint1B.CertificateRawData);
            Assert.AreEqual(dnsName2, certificate_t.GetNameInfo(X509NameType.DnsName, false));
            Assert.IsTrue(serverEndPoint1B.IsDefault);

            //-----------
            // check: update without new certificate
            //-----------
            await serverEndPointController.Update(TestInit1.ProjectId, publicEndPoint: publicEndPoint1, accessTokenGroupId: TestInit1.AccessTokenGroupId_2, null, null, makeDefault: true);
            serverEndPoint1B = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint1);
            certificate_t = new X509Certificate2(serverEndPoint1B.CertificateRawData);
            Assert.AreEqual(dnsName2, certificate_t.GetNameInfo(X509NameType.DnsName, false));
            Assert.IsTrue(serverEndPoint1B.IsDefault);
        }

        [TestMethod]
        public async Task CreateFromCertificate()
        {
            var publicEndPoint1 = TestInit1.ServerEndPoint_New1.ToString();
            var accessTokenGroupId1 = TestInit1.AccessTokenGroupId_1;

            // create certificate raw data
            var certificate1 = CertificateUtil.CreateSelfSigned();
            var dnsName1 = certificate1.GetNameInfo(X509NameType.DnsName, false);
            var certificateRawData1 = certificate1.Export(X509ContentType.Pfx, "123");

            // create certificate
            var serverEndPointController = TestInit.CreateServerEndPointController();
            var serverEndPoint1A = await serverEndPointController.CreateFromCertificate(TestInit1.ProjectId, publicEndPoint: publicEndPoint1,
                accessTokenGroupId: accessTokenGroupId1, certificateRawData: certificateRawData1, password: "123", makeDefault: false);

            //-----------
            // check: expect AlreadyExistsException
            //-----------
            try
            {
                await serverEndPointController.CreateFromCertificate(TestInit1.ProjectId, publicEndPoint: publicEndPoint1, 
                    accessTokenGroupId: accessTokenGroupId1, certificateRawData: certificateRawData1, password: "123", makeDefault: false);
                Assert.Fail("Exception Expected!");
            }
            catch (Exception ex) when (AccessUtil.IsAlreadyExistsException(ex))
            {}

            var serverEndPoint1B = await serverEndPointController.Get(TestInit1.ProjectId, serverEndPoint1A.PulicEndPoint);
            var certificate_t = new X509Certificate2(serverEndPoint1B.CertificateRawData);
            Assert.AreEqual(serverEndPoint1A.PulicEndPoint, serverEndPoint1B.PulicEndPoint);
            Assert.AreEqual(dnsName1, certificate_t.GetNameInfo(X509NameType.DnsName, false));
        }

    }
}
