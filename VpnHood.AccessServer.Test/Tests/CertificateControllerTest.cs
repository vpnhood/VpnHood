using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class CertificateControllerTest : ControllerTest
{
    [TestMethod]
    public async Task Crud()
    {
        var certificateController = new CertificateController(TestInit1.Http);

        //-----------
        // Create Certificate using RawData
        //-----------
        var x509Certificate = CertificateUtil.CreateSelfSigned("CN=1234.com");
        const string? password = "123";

        var certificate = await certificateController.CreateAsync(TestInit1.ProjectId, new CertificateCreateParams
        {
            RawData = x509Certificate.Export(X509ContentType.Pfx, password),
            Password = password
        });

        // get
        var certificate2 = await certificateController.GetAsync(TestInit1.ProjectId, certificate.CertificateId);
        var x509Certificate2 = new X509Certificate2(certificate2.RawData);
        Assert.AreEqual(x509Certificate.GetNameInfo(X509NameType.DnsName, false), x509Certificate2.GetNameInfo(X509NameType.DnsName, false));

        //-----------
        // Create Certificate using subject name
        //-----------
        certificate = await certificateController.CreateAsync(TestInit1.ProjectId, new CertificateCreateParams());
        Assert.IsNotNull(certificate.CommonName);
        Assert.IsTrue(certificate.ExpirationTime > DateTime.UtcNow);

        //-----------
        // Update a certificate
        //-----------
        certificate = await certificateController.UpdateAsync(TestInit1.ProjectId, certificate.CertificateId, new CertificateUpdateParams
        {
            RawData = new PatchOfByteOf() {Value = x509Certificate.Export(X509ContentType.Pfx, password)},
            Password = new PatchOfString() {Value =  password}
        });

        certificate2 = await certificateController.GetAsync(TestInit1.ProjectId, certificate.CertificateId);
        x509Certificate2 = new X509Certificate2(certificate2.RawData);
        Assert.AreEqual(x509Certificate.GetNameInfo(X509NameType.DnsName, false), x509Certificate2.GetNameInfo(X509NameType.DnsName, false));

        //-----------
        // Delete a certificate
        //-----------
        await certificateController.DeleteAsync(TestInit1.ProjectId, certificate.CertificateId);
        try
        {
            await certificateController.GetAsync(TestInit1.ProjectId, certificate.CertificateId);
        }
        catch (ApiException ex) when (ex.IsNotExistsException)
        {
            // ignore
        }

        //-----------
        // list
        //-----------
        var certificates = await certificateController.ListAsync(TestInit1.ProjectId);
        Assert.IsTrue(certificates.Count > 0);
        Assert.IsFalse(certificates.Any(x => x.RawData.Length > 0));
    }
}