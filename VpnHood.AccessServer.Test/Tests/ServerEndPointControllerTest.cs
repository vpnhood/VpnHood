using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.DTOs;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Tests
{
    [TestClass]
    public class ServerEndPointControllerTest : ControllerTest
    {

        [TestMethod]
        public async Task Crud()
        {
            const string dnsName = "Test_CRUD";
            var serverEndPointController = TestInit.CreateServerEndPointController();
            var publicEndPoint1 = TestInit1.HostEndPointNew1.ToString();
            var serverEndPoint1 = await serverEndPointController.Create(TestInit1.ProjectId, publicEndPoint1,
                new ServerEndPointCreateParams { AccessTokenGroupId = TestInit1.AccessTokenGroupId2, SubjectName = $"CN={dnsName}", MakeDefault = true });

            //-----------
            // check: accessTokenGroupId is created
            //-----------
            var serverEndPoint1B = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint1);
            Assert.AreEqual(serverEndPoint1B.PublicEndPoint, serverEndPoint1.PublicEndPoint);
            Assert.AreEqual(dnsName, serverEndPoint1B.CertificateCommonName);
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
            var publicEndPoint2 = TestInit1.HostEndPointNew2;
            var serverEndPoint2 = await serverEndPointController.Create(TestInit1.ProjectId, publicEndPoint2.ToString(),
                new ServerEndPointCreateParams { AccessTokenGroupId = TestInit1.AccessTokenGroupId2, SubjectName = $"CN={dnsName}", MakeDefault = true });
            serverEndPoint1 = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint1);

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
            await serverEndPointController.Create(TestInit1.ProjectId, publicEndPoint1,
                new ServerEndPointCreateParams { SubjectName = $"CN={dnsName}", AccessTokenGroupId = TestInit1.AccessTokenGroupId2, MakeDefault = false });
            serverEndPoint1 = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint1);
            Assert.IsFalse(serverEndPoint1.IsDefault, "ServerEndPoint1 should not be default");
            Assert.IsTrue(serverEndPoint2.IsDefault, "ServerEndPoint2 must be default");

            //-----------
            // check: first created endPoint in group must be default even if isDefault is false
            //-----------
            var accessTokenGroupController = TestInit.CreateAccessTokenGroupController();
            var accessTokenGroup = await accessTokenGroupController.Create(TestInit1.ProjectId, null);
            var publicEndPoint3 = TestInit1.HostEndPointNew3;
            await serverEndPointController.Create(TestInit1.ProjectId, publicEndPoint3.ToString(),
                new ServerEndPointCreateParams { SubjectName = $"CN={dnsName}2", AccessTokenGroupId = accessTokenGroup.AccessTokenGroupId, MakeDefault = false });
            var serverEndPoint3 = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint3.ToString());
            Assert.IsTrue(serverEndPoint3.IsDefault);

            //-----------
            // check: update with new certificate
            //-----------
            var certificate2 = CertificateUtil.CreateSelfSigned();
            var certificateRawData2 = certificate2.Export(X509ContentType.Pfx, "123");
            var dnsName2 = certificate2.GetNameInfo(X509NameType.DnsName, false);
            await serverEndPointController.Update(
                TestInit1.ProjectId,
                publicEndPoint1,
                new ServerEndPointUpdateParams { AccessTokenGroupId = TestInit1.AccessTokenGroupId2, CertificateRawData = certificateRawData2, CertificatePassword = "123", MakeDefault = true });
            serverEndPoint1B = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint1);
            var certificateT = new X509Certificate2(serverEndPoint1B.CertificateRawData);
            Assert.AreEqual(dnsName2, certificateT.GetNameInfo(X509NameType.DnsName, false));
            Assert.IsTrue(serverEndPoint1B.IsDefault);

            //-----------
            // check: update without new certificate
            //-----------
            var privateEndPoint = await TestInit.NewEndPoint();
            await serverEndPointController.Update(TestInit1.ProjectId, publicEndPoint1, new ServerEndPointUpdateParams { AccessTokenGroupId = TestInit1.AccessTokenGroupId2, PrivateEndPoint = privateEndPoint.ToString(),  MakeDefault = true });
            serverEndPoint1B = await serverEndPointController.Get(TestInit1.ProjectId, publicEndPoint1);
            certificateT = new X509Certificate2(serverEndPoint1B.CertificateRawData);
            Assert.AreEqual(dnsName2, certificateT.GetNameInfo(X509NameType.DnsName, false));
            Assert.AreEqual(privateEndPoint.ToString(), serverEndPoint1B.PrivateEndPoint);
            Assert.IsTrue(serverEndPoint1B.IsDefault);
        }

        [TestMethod]
        public async Task CreateFromCertificate()
        {
            var publicEndPoint1 = TestInit1.HostEndPointNew1.ToString();
            var accessTokenGroupId1 = TestInit1.AccessTokenGroupId1;

            // create certificate raw data
            var certificate1 = CertificateUtil.CreateSelfSigned();
            var dnsName1 = certificate1.GetNameInfo(X509NameType.DnsName, false);
            var certificateRawData1 = certificate1.Export(X509ContentType.Pfx, "123");

            // create certificate
            var serverEndPointController = TestInit.CreateServerEndPointController();
            var serverEndPoint1A = await serverEndPointController.Create(TestInit1.ProjectId, publicEndPoint1,
                new ServerEndPointCreateParams { AccessTokenGroupId = accessTokenGroupId1, CertificateRawData = certificateRawData1, CertificatePassword = "123", MakeDefault = false });

            //-----------
            // check: expect AlreadyExistsException
            //-----------
            try
            {
                await serverEndPointController.Create(TestInit1.ProjectId, publicEndPoint1,
                    new ServerEndPointCreateParams { AccessTokenGroupId = accessTokenGroupId1, CertificateRawData = certificateRawData1, CertificatePassword = "123", MakeDefault = false });
                Assert.Fail("Exception Expected!");
            }
            catch (Exception ex) when (AccessUtil.IsAlreadyExistsException(ex))
            { }

            var serverEndPoint1B = await serverEndPointController.Get(TestInit1.ProjectId, serverEndPoint1A.PublicEndPoint);
            var certificateT = new X509Certificate2(serverEndPoint1B.CertificateRawData);
            Assert.AreEqual(serverEndPoint1A.PublicEndPoint, serverEndPoint1B.PublicEndPoint);
            Assert.AreEqual(dnsName1, certificateT.GetNameInfo(X509NameType.DnsName, false));
        }

    }
}
