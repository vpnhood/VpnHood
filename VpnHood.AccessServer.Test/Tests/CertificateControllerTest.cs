using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.DTOs;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class CertificateControllerTest : ControllerTest
{
    [TestMethod]
    public async Task Crud()
    {
        var certificateController = TestInit1.CreateCertificateController();

        //-----------
        // Create Certificate using RawData
        //-----------
        var x509Certificate = CertificateUtil.CreateSelfSigned("CN=1234.com");
        const string? password = "123";

        var certificate = await certificateController.Create(TestInit1.ProjectId, new CertificateCreateParams
        {
            RawData = x509Certificate.Export(X509ContentType.Pfx, password),
            Password = password
        });

        // get
        var certificate2 = await certificateController.Get(TestInit1.ProjectId, certificate.CertificateId);
        var x509Certificate2 = new X509Certificate2(certificate2.RawData);
        Assert.AreEqual(x509Certificate.GetNameInfo(X509NameType.DnsName, false), x509Certificate2.GetNameInfo(X509NameType.DnsName, false));

        //-----------
        // Create Certificate using subject name
        //-----------
        certificate = await certificateController.Create(TestInit1.ProjectId, null);
        Assert.IsNotNull(certificate.CommonName);
        Assert.IsTrue(certificate.ExpirationTime > DateTime.UtcNow);

        //-----------
        // Update a certificate
        //-----------
        certificate = await certificateController.Update(TestInit1.ProjectId, certificate.CertificateId, new CertificateUpdateParams
        {
            RawData = x509Certificate.Export(X509ContentType.Pfx, password),
            Password = password
        });

        certificate2 = await certificateController.Get(TestInit1.ProjectId, certificate.CertificateId);
        x509Certificate2 = new X509Certificate2(certificate2.RawData);
        Assert.AreEqual(x509Certificate.GetNameInfo(X509NameType.DnsName, false), x509Certificate2.GetNameInfo(X509NameType.DnsName, false));

        //-----------
        // Delete a certificate
        //-----------
        await certificateController.Delete(TestInit1.ProjectId, certificate.CertificateId);
        try
        {
            await certificateController.Get(TestInit1.ProjectId, certificate.CertificateId);
        }
        catch (Exception ex) when (AccessUtil.IsNotExistsException(ex))
        {
            // ignore
        }

        //-----------
        // list
        //-----------
        var certificates = await certificateController.List(TestInit1.ProjectId);
        Assert.IsTrue(certificates.Length > 0);
        Assert.IsFalse(certificates.Any(x => x.RawData.Length > 0));
    }
}