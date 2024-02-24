using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.AccessServer.Test.Helper;
using VpnHood.Server;
using VpnHood.Server.Access;
using ServerState = VpnHood.AccessServer.Api.ServerState;

namespace VpnHood.AccessServer.Test.Tests;

[TestClass]
public class CertificateTest
{
    [TestMethod]
    public async Task Crud()
    {
        var farm = await ServerFarmDom.Create();

        //-----------
        // Create Certificate using RawData
        //-----------
        var x509Certificate = CertificateUtil.CreateSelfSigned("CN=1234.com,O=Foo");
        const string? password = "123";
        var certificate = await farm.CertificateImport(new CertificateImportParams
        {
            RawData = x509Certificate.Export(X509ContentType.Pfx, password),
            Password = password
        });

        Assert.AreEqual(x509Certificate.GetNameInfo(X509NameType.DnsName, false), certificate.CommonName);
        Assert.AreEqual(x509Certificate.Thumbprint, certificate.Thumbprint);

        // get
        await farm.Reload();
        Assert.AreEqual(x509Certificate.GetNameInfo(X509NameType.DnsName, false),
            farm.ServerFarm.Certificate?.CommonName);

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

        certificate = await farm.CertificateReplace(new CertificateCreateParams
        {
            CertificateSigningRequest = csr,
            ExpirationTime = expirationTime
        });
        Assert.IsFalse(string.IsNullOrEmpty(certificate.Thumbprint));
        Assert.IsFalse(certificate.IsTrusted);
        Assert.IsTrue(certificate.ExpirationTime > DateTime.UtcNow.AddDays(6) &&
                      certificate.ExpirationTime < DateTime.UtcNow.AddDays(8));
        Assert.IsTrue(certificate.IssueTime > DateTime.UtcNow.AddDays(-1));

        await farm.Reload();
        certificate = farm.ServerFarm.Certificate!;
        Assert.AreEqual(csr.CommonName, certificate.CommonName);
        Assert.IsTrue(certificate.SubjectName.Contains($"CN={csr.CommonName}"));
        Assert.IsTrue(certificate.SubjectName.Contains($"O={csr.Organization}"));
        Assert.IsTrue(certificate.SubjectName.Contains($"OU={csr.OrganizationUnit}"));
        Assert.IsTrue(certificate.SubjectName.Contains($"C={csr.LocationCountry}"));
        Assert.IsTrue(certificate.SubjectName.Contains($"S={csr.LocationState}"));
        Assert.IsTrue(certificate.SubjectName.Contains($"L={csr.LocationCity}"));
    }

    [TestMethod]
    public async Task Renew()
    {
        // create server and start listen
        using var http01ChallengeService = new Http01ChallengeService([IPAddress.Loopback], TestAcmeOrderService.TestToken, TestAcmeOrderService.TestKeyAuthorization);
        http01ChallengeService.Start();

        // create farm and server
        var farm = await ServerFarmDom.Create(serverCount: 0);
        var server = await farm.AddNewServer();

        await server.Reload();
        Assert.AreEqual(ServerState.Idle, server.Server.ServerState);

    
        // create new certificate
        await farm.CertificateReplace(new CertificateCreateParams()
        {
            CertificateSigningRequest = new CertificateSigningRequest
            {
                CommonName = "localhost"
            }
        });

        // wait for server configuring
        await server.Reload();
        Assert.AreEqual(ServerState.Configuring, server.Server.ServerState);

        // configure server
        await server.Configure();
        await server.Reload();
        Assert.AreEqual(ServerState.Idle, server.Server.ServerState);

        // renew
        await farm.CertificateRenew();
        await farm.Reload();
        Assert.IsTrue(farm.ServerFarm.Certificate!.IsTrusted);
    }
}