using System.Security.Cryptography.X509Certificates;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.Common.Client;
using VpnHood.Common.Exceptions;
using VpnHood.Server.Access;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class CertificateTest
{
    [TestMethod]
    public async Task Crud()
    {
        var testApp = await TestApp.Create();
        var certificateClient = testApp.CertificatesClient;

        //-----------
        // Create Certificate using RawData
        //-----------
        var x509Certificate = CertificateUtil.CreateSelfSigned("CN=1234.com,O=Foo");
        const string? password = "123";

        var certificate = await certificateClient.CreateByImportAsync(testApp.ProjectId, new CertificateImportParams
        {
            RawData = x509Certificate.Export(X509ContentType.Pfx, password),
            Password = password
        });

        var x509Certificate2 = new X509Certificate2(certificate.RawData!);
        Assert.AreEqual(x509Certificate.GetNameInfo(X509NameType.DnsName, false), x509Certificate2.GetNameInfo(X509NameType.DnsName, false));

        // get
        var certificateData = await certificateClient.GetAsync(testApp.ProjectId, certificate.CertificateId);
        Assert.AreEqual(x509Certificate.GetNameInfo(X509NameType.DnsName, false), certificateData.Certificate.CommonName);

        //-----------
        // Create Certificate using subject name
        //-----------
        var expirationTime = DateTime.UtcNow.AddDays(7);
        var csr = new CertificateSigningRequest
        {
            CommonName = certificate.CommonName,
            Organization = Guid.NewGuid().ToString(),
            OrganizationUnit = Guid.NewGuid().ToString(),
            LocationCity = Guid.NewGuid().ToString(),
            LocationCountry = Guid.NewGuid().ToString(),
            LocationState = Guid.NewGuid().ToString()
        };
        certificate = await certificateClient.CreateBySelfSignedAsync(testApp.ProjectId, new CertificateSelfSignedParams
        {
            CertificateSigningRequest = csr, 
            ExpirationTime = expirationTime
        });
        Assert.IsFalse(string.IsNullOrEmpty(certificate.Thumbprint));
        Assert.IsFalse(certificate.IsVerified);
        Assert.IsTrue(certificate.ExpirationTime > DateTime.UtcNow.AddDays(6) && certificate.ExpirationTime < DateTime.UtcNow.AddDays(8));
        Assert.IsTrue(certificate.IssueTime > DateTime.UtcNow.AddDays(-1));

        certificate = (await certificateClient.GetAsync(testApp.ProjectId, certificate.CertificateId)).Certificate;
        Assert.AreEqual(csr.CommonName, certificate.CommonName);
        Assert.IsTrue(certificate.SubjectName.Contains($"CN={csr.CommonName}"));
        Assert.IsTrue(certificate.SubjectName.Contains($"O={csr.Organization}"));
        Assert.IsTrue(certificate.SubjectName.Contains($"OU={csr.OrganizationUnit}"));
        Assert.IsTrue(certificate.SubjectName.Contains($"C={csr.LocationCountry}"));
        Assert.IsTrue(certificate.SubjectName.Contains($"S={csr.LocationState}"));
        Assert.IsTrue(certificate.SubjectName.Contains($"L={csr.LocationCity}"));

        //-----------
        // Delete a certificate
        //-----------
        await certificateClient.DeleteAsync(testApp.ProjectId, certificate.CertificateId);
        try
        {
            await certificateClient.GetAsync(testApp.ProjectId, certificate.CertificateId);
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }

        //-----------
        // list
        //-----------
        var certificates = await certificateClient.ListAsync(testApp.ProjectId);
        Assert.IsTrue(certificates.Count > 0);
        Assert.IsFalse(certificates.Any(x => x.Certificate.RawData != null));
    }
}