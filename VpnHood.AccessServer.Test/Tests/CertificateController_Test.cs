using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Transactions;
using VpnHood.Server;
using VpnHood.AccessServer.Services;
using System.Net;
using System.Data.SqlClient;

namespace VpnHood.AccessServer.Test
{
    [TestClass]
    public class CertificateController_Test
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
            var certificateController = TestUtil.CreateCertificateController();
            var dnsName = "Test_CRUD";
            await certificateController.Create(TestInit.TEST_ServerEndPoint1, $"CN={dnsName}");

            var accessController = TestUtil.CreateAccessController();
            var certBuffer = await accessController.GetSslCertificateData(TestInit.TEST_ServerEndPoint1);
            var certificate = new X509Certificate2(certBuffer);
            Assert.AreEqual(dnsName, certificate.GetNameInfo(X509NameType.DnsName, false));

            //-----------
            // check: delete
            //-----------
            await certificateController.Delete(TestInit.TEST_ServerEndPoint1);

            try
            {
                certBuffer = await accessController.GetSslCertificateData(TestInit.TEST_ServerEndPoint1);
                Assert.Fail("KeyNotFoundException expected!");
            }
            catch (KeyNotFoundException) { }
        }

        [TestMethod]
        public async Task Import()
        {
            var serverEndPoint = TestInit.TEST_ServerEndPoint2;

            var certificate1 = CertificateUtil.CreateSelfSigned();
            var dnsName = certificate1.GetNameInfo(X509NameType.DnsName, false);
            var rawData = certificate1.Export(X509ContentType.Pfx, "123");

            var certificateController = TestUtil.CreateCertificateController();
            await certificateController.Import(serverEndPoint: serverEndPoint, rawData: rawData, password: "123");

            var accessController = TestUtil.CreateAccessController();
            rawData = await accessController.GetSslCertificateData(serverEndPoint);
            var certificate_t = new X509Certificate2(rawData);
            Assert.AreEqual(dnsName, certificate_t.GetNameInfo(X509NameType.DnsName, false));


            // ******
            // Test: overwrite is false
            var certificate2 = CertificateUtil.CreateSelfSigned();
            var rawData2 = certificate2.Export(X509ContentType.Pfx, "123");
            var dnsName2 = certificate2.GetNameInfo(X509NameType.DnsName, false);
            try
            {
                await certificateController.Import(serverEndPoint: serverEndPoint, rawData: rawData2, password: "123");
                Assert.Fail("Exception Expect");
            }
            catch { }
            rawData = await accessController.GetSslCertificateData(serverEndPoint);
            certificate_t = new X509Certificate2(rawData);
            Assert.AreEqual(dnsName, certificate_t.GetNameInfo(X509NameType.DnsName, false));

            // ******
            // Test: overwrite is true
            await certificateController.Import(serverEndPoint: serverEndPoint, rawData: rawData2, password: "123", overwrite: true);
            rawData = await accessController.GetSslCertificateData(serverEndPoint);
            certificate_t = new X509Certificate2(rawData);
            Assert.AreEqual(dnsName2, certificate_t.GetNameInfo(X509NameType.DnsName, false));
        }

    }
}
