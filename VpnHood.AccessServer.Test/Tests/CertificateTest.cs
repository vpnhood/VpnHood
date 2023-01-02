using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.Common.Client;
using VpnHood.Common.Exceptions;
using VpnHood.Server;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class CertificateTest : BaseTest
{
    [TestMethod]
    public async Task Crud()
    {
        var certificateClient = TestInit1.CertificatesClient;

        //-----------
        // Create Certificate using RawData
        //-----------
        var x509Certificate = CertificateUtil.CreateSelfSigned("CN=1234.com");
        const string? password = "123";

        var certificate = await certificateClient.CreateAsync(TestInit1.ProjectId, new CertificateCreateParams
        {
            RawData = x509Certificate.Export(X509ContentType.Pfx, password),
            Password = password
        });

        var x509Certificate2 = new X509Certificate2(certificate.RawData!);
        Assert.AreEqual(x509Certificate.GetNameInfo(X509NameType.DnsName, false), x509Certificate2.GetNameInfo(X509NameType.DnsName, false));

        // get
        certificate = await certificateClient.GetAsync(TestInit1.ProjectId, certificate.CertificateId);
        Assert.AreEqual(x509Certificate.GetNameInfo(X509NameType.DnsName, false), certificate.CommonName);

        //-----------
        // Create Certificate using subject name
        //-----------
        certificate = await certificateClient.CreateAsync(TestInit1.ProjectId, new CertificateCreateParams());
        Assert.IsNotNull(certificate.CommonName);
        Assert.IsTrue(certificate.ExpirationTime > DateTime.UtcNow);

        //-----------
        // Update a certificate
        //-----------
        certificate = await certificateClient.UpdateAsync(TestInit1.ProjectId, certificate.CertificateId, new CertificateUpdateParams
        {
            RawData = new PatchOfByteOf { Value = x509Certificate.Export(X509ContentType.Pfx, password) },
            Password = new PatchOfString { Value = password }
        });

        certificate = await certificateClient.GetAsync(TestInit1.ProjectId, certificate.CertificateId);
        Assert.AreEqual(x509Certificate.GetNameInfo(X509NameType.DnsName, false), certificate.CommonName);

        //-----------
        // Delete a certificate
        //-----------
        await certificateClient.DeleteAsync(TestInit1.ProjectId, certificate.CertificateId);
        try
        {
            await certificateClient.GetAsync(TestInit1.ProjectId, certificate.CertificateId);
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }

        //-----------
        // list
        //-----------
        var certificates = await certificateClient.ListAsync(TestInit1.ProjectId);
        Assert.IsTrue(certificates.Count > 0);
        Assert.IsFalse(certificates.Any(x => x.RawData != null));
    }
}