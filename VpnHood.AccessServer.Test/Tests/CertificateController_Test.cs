using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Transactions;
using VpnHood.Server;
using VpnHood.AccessServer.Services;
using System.Net;

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
            await certificateController.Create("10.10.100.1", $"CN={dnsName}");

            var accessController = TestUtil.CreateAccessController();
            var certBuffer = await accessController.GetSslCertificateData("10.10.100.1");
            var certificate = new X509Certificate2(certBuffer);
            Assert.AreEqual(dnsName, certificate.GetNameInfo(X509NameType.DnsName, false));

            //-----------
            // check: delete
            //-----------
            await certificateController.Delete("10.10.100.1");

            try
            {
                certBuffer = await accessController.GetSslCertificateData("10.10.100.1");
                Assert.Fail("KeyNotFoundException expected!");
            }
            catch (KeyNotFoundException) {}
        }

        [TestMethod]
        public async Task Import()
        {
            var certificate = CertificateUtil.CreateSelfSigned();
            var dnsName = certificate.GetNameInfo(X509NameType.DnsName, false);
            var rawData = certificate.Export(X509ContentType.Pfx, "123");

            var certificateController = TestUtil.CreateCertificateController();
            await certificateController.Import(serverEndPoint: "10.10.100.2:443", rawData: rawData, password: "123");

            var accessController = TestUtil.CreateAccessController();
            var rawData2 = await accessController.GetSslCertificateData("10.10.100.2:443");
            var certificate2 = new X509Certificate2(rawData2);
            Assert.AreEqual(dnsName, certificate2.GetNameInfo(X509NameType.DnsName, false));
        }
     
    }
}
