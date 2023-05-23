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
public class CertificateTest
{
    [TestMethod]
    public async Task Crud()
    {
        var testInit = await TestInit.Create();
        var certificateClient = testInit.CertificatesClient;

        //-----------
        // Create Certificate using RawData
        //-----------
        var x509Certificate = CertificateUtil.CreateSelfSigned("CN=1234.com");
        const string? password = "123";

        var certificate = await certificateClient.CreateByImportAsync(testInit.ProjectId, new CertificateImportParams
        {
            RawData = x509Certificate.Export(X509ContentType.Pfx, password),
            Password = password
        });

        var x509Certificate2 = new X509Certificate2(certificate.RawData!);
        Assert.AreEqual(x509Certificate.GetNameInfo(X509NameType.DnsName, false), x509Certificate2.GetNameInfo(X509NameType.DnsName, false));

        // get
        var certificateData = await certificateClient.GetAsync(testInit.ProjectId, certificate.CertificateId);
        Assert.AreEqual(x509Certificate.GetNameInfo(X509NameType.DnsName, false), certificateData.Certificate.CommonName);

        //-----------
        // Create Certificate using subject name
        //-----------
        certificate = await certificateClient.CreateBySelfSignedAsync(testInit.ProjectId,
            new CertificateSelfSignedParams { SubjectName = $"CN={certificate.CommonName}" });
        Assert.IsNotNull(certificate.CommonName);
        Assert.IsTrue(certificate.ExpirationTime > DateTime.UtcNow);

        //-----------
        // Update a certificate
        //-----------
        certificate = await certificateClient.ReplaceByImportAsync(testInit.ProjectId, certificate.CertificateId,
            new CertificateImportParams
            {
                RawData = x509Certificate.Export(X509ContentType.Pfx, password),
                Password = password
            });

        certificate = (await certificateClient.GetAsync(testInit.ProjectId, certificate.CertificateId)).Certificate;
        Assert.AreEqual(x509Certificate.GetNameInfo(X509NameType.DnsName, false), certificate.CommonName);

        //-----------
        // Delete a certificate
        //-----------
        await certificateClient.DeleteAsync(testInit.ProjectId, certificate.CertificateId);
        try
        {
            await certificateClient.GetAsync(testInit.ProjectId, certificate.CertificateId);
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(NotExistsException), ex.ExceptionTypeName);
        }

        //-----------
        // list
        //-----------
        var certificates = await certificateClient.ListAsync(testInit.ProjectId);
        Assert.IsTrue(certificates.Count > 0);
        Assert.IsFalse(certificates.Any(x => x.Certificate.RawData != null));
    }

    [TestMethod]
    public async Task Fail_updating_certificate_with_another_common_name()
    {
        var testInit = await TestInit.Create();
        var certificateClient = testInit.CertificatesClient;

        //-----------
        // Create Certificate using RawData
        //-----------
        var x509Certificate = CertificateUtil.CreateSelfSigned("CN=1234.com");
        const string? password = "123";

        var certificate = await certificateClient.CreateByImportAsync(testInit.ProjectId, new CertificateImportParams
        {
            RawData = x509Certificate.Export(X509ContentType.Pfx, password),
            Password = password
        });


        //-----------
        // Update a certificate
        //-----------
        //-----------
        // Create Certificate using RawData
        //-----------
        x509Certificate = CertificateUtil.CreateSelfSigned("CN=zz.1234.com");
        try
        {
            await certificateClient.ReplaceByImportAsync(testInit.ProjectId, certificate.CertificateId,
                new CertificateImportParams
                {
                    RawData = x509Certificate.Export(X509ContentType.Pfx, password: "")
                });
            Assert.Fail("Invalid Operation is expected");
        }
        catch (ApiException ex)
        {
            Assert.AreEqual(nameof(InvalidOperationException), ex.ExceptionTypeName);
        }

    }
}