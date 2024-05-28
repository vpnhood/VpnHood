using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using GrayMint.Common.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VpnHood.AccessServer.Api;
using VpnHood.AccessServer.Options;
using VpnHood.AccessServer.Test.Dom;
using VpnHood.Common.Logging;
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
        using var farm = await ServerFarmDom.Create(serverCount: 1);

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
        Assert.IsFalse(certificate.IsValidated);
        Assert.IsTrue(certificate.ExpirationTime > DateTime.UtcNow.AddDays(6) &&
                      certificate.ExpirationTime < DateTime.UtcNow.AddDays(8));
        Assert.IsTrue(certificate.IssueTime > DateTime.UtcNow.AddDays(-1));

        await farm.Reload();
        var server = await farm.AddNewServer();
        var x509Certificate2 = new X509Certificate2(server.ServerConfig.Certificates.First().RawData);

        var subject = x509Certificate2.Subject;
        Assert.AreEqual(csr.CommonName, Regex.Match(subject, @"CN=([^,]+)").Groups[1].Value);
        Assert.AreEqual(csr.Organization, Regex.Match(subject, @"O=([^,]+)").Groups[1].Value);
        Assert.AreEqual(csr.OrganizationUnit, Regex.Match(subject, @"OU=([^,]+)").Groups[1].Value);
        Assert.AreEqual(csr.LocationCountry, Regex.Match(subject, @"C=([^,]+)").Groups[1].Value);
        Assert.AreEqual(csr.LocationState, Regex.Match(subject, @"S=([^,]+)").Groups[1].Value);
        Assert.AreEqual(csr.LocationCity, Regex.Match(subject, @"L=([^,]+)").Groups[1].Value);
    }

    [TestMethod]
    public async Task Validate()
    {
        // create farm and server
        using var farm = await ServerFarmDom.Create(serverCount: 0);
        farm.TestApp.AppOptions.ServerUpdateStatusInterval = TimeSpan.FromSeconds(3);
        farm.TestApp.AgentTestApp.AgentOptions.ServerUpdateStatusInterval = TimeSpan.FromSeconds(3);
        var server = await farm.AddNewServer();

        await server.Reload();
        Assert.AreEqual(ServerState.Idle, server.Server.ServerState);
        Assert.IsFalse(farm.ServerFarm.UseHostName);

        // create new certificate
        await farm.CertificateReplace(new CertificateCreateParams
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

        // renew order
        await farm.Update(new ServerFarmUpdateParams { AutoValidateCertificate = new PatchOfBoolean { Value = true } });
        await server.WaitForState(ServerState.Configuring);

        // configure server
        await server.Configure(false);
        Assert.IsNotNull(server.ServerConfig.DnsChallenge);
        VhLogger.Instance = VhLogger.CreateConsoleLogger(true);
        using var http01ChallengeService = new Http01ChallengeService([IPAddress.Loopback],
            server.ServerConfig.DnsChallenge.Token, server.ServerConfig.DnsChallenge.KeyAuthorization,
            TimeSpan.FromMinutes(1));
        http01ChallengeService.Start();
        await Task.Delay(500); // wait for listening before server send the request 

        await server.SendStatus();

        // Wait for server to be idle
        await server.WaitForState(ServerState.Idle);

        // Wait for signin the certificate
        await TestUtil.AssertEqualsWait(true, async () =>
        {
            await farm.Reload();
            return farm.ServerFarm.Certificate!.IsValidated;
        });

        Assert.IsTrue(farm.ServerFarm.UseHostName);

        // Create a token and make sure it is valid and there is no CertificateHash
        var accessToken = await farm.CreateAccessToken();
        var token = await accessToken.GetToken();
        Assert.IsNull(token.ServerToken.CertificateHash);
    }

    [TestMethod]
    public async Task ValidateJob()
    {
        var testApp = await TestApp.Create(new Dictionary<string, string?>
        {
            [$"CertificateValidator:{nameof(CertificateValidatorOptions.Interval)}"] = TimeSpan.FromSeconds(1).ToString(),
            [$"CertificateValidator:{nameof(CertificateValidatorOptions.Due)}"] = TimeSpan.FromSeconds(1).ToString()
        });

        // disable old AutoValidate to prevent jon validate old certificates in test
        var validatingCertificates = await testApp.VhContext.Certificates
            .Where(x=>x.AutoValidate).ToArrayAsync();
        foreach (var validatingCertificate in validatingCertificates)
            validatingCertificate.AutoValidate = false;
        await testApp.VhContext.SaveChangesAsync();

        // create farm and server
        using var farm = await ServerFarmDom.Create(testApp, serverCount: 0);
        Assert.IsFalse(farm.ServerFarm.UseHostName);
        farm.TestApp.AppOptions.ServerUpdateStatusInterval = TimeSpan.FromSeconds(4);
        farm.TestApp.AgentTestApp.AgentOptions.ServerUpdateStatusInterval = TimeSpan.FromSeconds(4);
        Assert.IsNotNull(farm.ServerFarm.Certificate);
        var server = await farm.AddNewServer();

        // create new certificate
        await farm.CertificateReplace(new CertificateCreateParams
        {
            CertificateSigningRequest = new CertificateSigningRequest
            {
                CommonName = "localhost"
            }
        });

        // configure server to the latest config
        await server.Configure();
        Assert.AreEqual(ServerState.Idle, server.Server.ServerState);

        // set AutoValidate 
        await farm.Update(new ServerFarmUpdateParams { AutoValidateCertificate = new PatchOfBoolean { Value = true } });
        await server.WaitForState(ServerState.Configuring);
        await server.Configure();
        await server.WaitForState(ServerState.Idle);

        // wait for auto validate to fail
        testApp.Logger.LogInformation("Test: Waiting for validation to fail.");
        await TestUtil.AssertEqualsWait(false, async () =>
        {
            await farm.Reload();
            return string.IsNullOrEmpty(farm.ServerFarm.Certificate.ValidateError);
        });
        Assert.IsFalse(farm.ServerFarm.Certificate.ValidateInprogress);
        Assert.IsTrue(farm.ServerFarm.Certificate.ValidateErrorCount > 0);
        Assert.IsNotNull(farm.ServerFarm.Certificate.ValidateErrorTime);
        Assert.AreEqual(0, farm.ServerFarm.Certificate.ValidateCount);

        // wait for auto validating
        testApp.Logger.LogInformation("Test: Waiting for ValidateInprogress to be true.");
        await TestUtil.AssertEqualsWait(true, async () =>
        {
            await farm.Reload();
            return farm.ServerFarm.Certificate.ValidateInprogress;
        });


        testApp.Logger.LogInformation("Test: start http01 challenge.");
        // start http01 challenge
        await server.Configure(false);
        Assert.IsNotNull(server.ServerConfig.DnsChallenge);
        using var http01ChallengeService = new Http01ChallengeService([IPAddress.Loopback],
            server.ServerConfig.DnsChallenge.Token, server.ServerConfig.DnsChallenge.KeyAuthorization,
            TimeSpan.FromMinutes(1));
        http01ChallengeService.Start();
        await Task.Delay(500); // wait for listening before server send the request 

        await server.SendStatus();

        // wait for auto validating
        testApp.Logger.LogInformation("Test: Waiting for ValidateInprogress to be false.");
        Assert.IsNotNull(farm.ServerFarm.Certificate);
        await TestUtil.AssertEqualsWait(false, async () =>
        {
            await farm.Reload();
            return farm.ServerFarm.Certificate.ValidateInprogress;
        });
        Assert.IsTrue(farm.ServerFarm.UseHostName);
        Assert.IsTrue(farm.ServerFarm.Certificate.IsValidated);
        Assert.IsNull(farm.ServerFarm.Certificate.ValidateErrorTime);
        Assert.IsFalse(farm.ServerFarm.Certificate.ValidateInprogress);
        Assert.AreEqual(0, farm.ServerFarm.Certificate.ValidateErrorCount);
        Assert.AreEqual(1, farm.ServerFarm.Certificate.ValidateCount);
        Assert.IsNull(farm.ServerFarm.Certificate.ValidateError);
    }
}