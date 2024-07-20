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
        var certificate = await farm.CertificateImport(new CertificateImportParams {
            RawData = x509Certificate.Export(X509ContentType.Pfx, password),
            Password = password
        });

        Assert.AreEqual(x509Certificate.GetNameInfo(X509NameType.DnsName, false), certificate.CommonName);
        Assert.AreEqual(x509Certificate.Thumbprint, certificate.Thumbprint);

        // get
        await farm.Reload();
        Assert.AreEqual(x509Certificate.GetNameInfo(X509NameType.DnsName, false),
            farm.CertificateInToken.CommonName);

        //-----------
        // Create Certificate using subject name
        //-----------
        var expirationTime = DateTime.UtcNow.AddDays(7);
        var csr = new CertificateSigningRequest {
            CommonName = certificate.CommonName,
            Organization = Guid.NewGuid().ToString(),
            OrganizationUnit = Guid.NewGuid().ToString(),
            LocationCity = Guid.NewGuid().ToString(),
            LocationCountry = Guid.NewGuid().ToString(),
            LocationState = Guid.NewGuid().ToString()
        };

        certificate = await farm.CertificateReplace(new CertificateCreateParams {
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
        await farm.CertificateReplace(new CertificateCreateParams {
            CertificateSigningRequest = new CertificateSigningRequest {
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
        await TestUtil.AssertEqualsWait(true, async () => {
            await farm.Reload();
            return farm.CertificateInToken.IsValidated;
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
        var testApp = await TestApp.Create(new Dictionary<string, string?> {
            [$"CertificateValidator:{nameof(CertificateValidatorOptions.Interval)}"] =
                TimeSpan.FromSeconds(1).ToString(),
            [$"CertificateValidator:{nameof(CertificateValidatorOptions.Due)}"] = TimeSpan.FromSeconds(1).ToString()
        });

        // disable old AutoValidate to prevent jon validate old certificates in test
        var validatingCertificates = await testApp.VhContext.Certificates
            .Where(x => x.AutoValidate).ToArrayAsync();
        foreach (var validatingCertificate in validatingCertificates)
            validatingCertificate.AutoValidate = false;
        await testApp.VhContext.SaveChangesAsync();

        // create farm and server
        using var farm = await ServerFarmDom.Create(testApp, serverCount: 0);
        Assert.IsFalse(farm.ServerFarm.UseHostName);
        farm.TestApp.AppOptions.ServerUpdateStatusInterval = TimeSpan.FromSeconds(4);
        farm.TestApp.AgentTestApp.AgentOptions.ServerUpdateStatusInterval = TimeSpan.FromSeconds(4);
        var server = await farm.AddNewServer();

        // create new certificate
        await farm.CertificateReplace(new CertificateCreateParams {
            CertificateSigningRequest = new CertificateSigningRequest {
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
        await TestUtil.AssertEqualsWait(false, async () => {
            await farm.Reload();
            return string.IsNullOrEmpty(farm.CertificateInToken.ValidateError);
        });
        Assert.IsFalse(farm.CertificateInToken.ValidateInprogress);
        Assert.IsTrue(farm.CertificateInToken.ValidateErrorCount > 0);
        Assert.IsNotNull(farm.CertificateInToken.ValidateErrorTime);
        Assert.AreEqual(0, farm.CertificateInToken.ValidateCount);

        // wait for auto validating
        testApp.Logger.LogInformation("Test: Waiting for ValidateInprogress to be true.");
        await TestUtil.AssertEqualsWait(true, async () => {
            await farm.Reload();
            return farm.CertificateInToken.ValidateInprogress;
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
        await TestUtil.AssertEqualsWait(false, async () => {
            await farm.Reload();
            return farm.CertificateInToken.ValidateInprogress;
        });
        Assert.IsTrue(farm.ServerFarm.UseHostName);
        Assert.IsTrue(farm.CertificateInToken.IsValidated);
        Assert.IsNull(farm.CertificateInToken.ValidateErrorTime);
        Assert.IsFalse(farm.CertificateInToken.ValidateInprogress);
        Assert.AreEqual(0, farm.CertificateInToken.ValidateErrorCount);
        Assert.AreEqual(1, farm.CertificateInToken.ValidateCount);
        Assert.IsNull(farm.CertificateInToken.ValidateError);
    }

    [TestMethod]
    public async Task Certificate_history_should_not_have_duplicate_common_name()
    {
        var dnsName1 = $"{Guid.NewGuid()}.com";
        var farm = await ServerFarmDom.Create();
        await farm.Update(new ServerFarmUpdateParams { MaxCertificateCount = new PatchOfInteger { Value = 3 } });
        await farm.CertificateReplace(new CertificateCreateParams {
            CertificateSigningRequest = new CertificateSigningRequest { CommonName = dnsName1 }
        });

        await farm.CertificateReplace(new CertificateCreateParams {
            CertificateSigningRequest = new CertificateSigningRequest { CommonName = dnsName1 }
        });

        var certificates = await farm.CertificateList();
        Assert.AreEqual(2, certificates.Length);
    }

    [TestMethod]
    public async Task Changing_CertificateHistoryCount_should_delete_additions()
    {
        var dnsName1 = $"{Guid.NewGuid()}.com";
        var farm = await ServerFarmDom.Create();
        await farm.Update(new ServerFarmUpdateParams { MaxCertificateCount = new PatchOfInteger { Value = 3 } });
        await farm.CertificateReplace(new CertificateCreateParams {
            CertificateSigningRequest = new CertificateSigningRequest { CommonName = dnsName1 }
        });

        var dnsName2 = $"{Guid.NewGuid()}.com";
        await farm.CertificateReplace(new CertificateCreateParams {
            CertificateSigningRequest = new CertificateSigningRequest { CommonName = dnsName2 }
        });

        var dnsName3 = $"{Guid.NewGuid()}.com";
        await farm.CertificateReplace(new CertificateCreateParams {
            CertificateSigningRequest = new CertificateSigningRequest { CommonName = dnsName3 }
        });

        var certificates = await farm.CertificateList();
        Assert.AreEqual(3, certificates.Length);

        await farm.Update(new ServerFarmUpdateParams { MaxCertificateCount = new PatchOfInteger { Value = 2 } });
        certificates = await farm.CertificateList();
        Assert.AreEqual(2, certificates.Length);

        Assert.IsTrue(certificates.Any(x => dnsName2 == x.CommonName));
        Assert.IsTrue(certificates.Any(x => dnsName3 == x.CommonName && x.IsInToken));
    }

    [TestMethod]
    public async Task Previous_IsInToken_must_reset_by_create()
    {
        var dnsName1 = $"{Guid.NewGuid()}.com";
        var farm = await ServerFarmDom.Create();
        await farm.Update(new ServerFarmUpdateParams { MaxCertificateCount = new PatchOfInteger { Value = 2 } });
        await farm.CertificateReplace(new CertificateCreateParams {
            CertificateSigningRequest = new CertificateSigningRequest { CommonName = dnsName1 }
        });

        var dnsName2 = $"{Guid.NewGuid()}.com";
        await farm.CertificateReplace(new CertificateCreateParams {
            CertificateSigningRequest = new CertificateSigningRequest { CommonName = dnsName2 }
        });


        var certificates = await farm.CertificateList();
        Assert.AreEqual(2, certificates.Length);

        Assert.IsTrue(certificates.Any(x => dnsName1 == x.CommonName && !x.IsInToken));
        Assert.IsTrue(certificates.Any(x => dnsName2 == x.CommonName && x.IsInToken));
    }

    [TestMethod]
    public async Task Previous_IsInToken_must_reset_by_import()
    {
        var farm = await ServerFarmDom.Create();
        await farm.Update(new ServerFarmUpdateParams { MaxCertificateCount = new PatchOfInteger { Value = 2 } });

        var dnsName1 = $"{Guid.NewGuid()}.com";
        var x509Certificate1 = CertificateUtil.CreateSelfSigned($"CN={dnsName1},O=Foo");
        const string? password = "123";
        await farm.CertificateImport(new CertificateImportParams {
            RawData = x509Certificate1.Export(X509ContentType.Pfx, password),
            Password = password
        });

        var dnsName2 = $"{Guid.NewGuid()}.com";
        var x509Certificate2 = CertificateUtil.CreateSelfSigned($"CN={dnsName2},O=Foo");
        await farm.CertificateImport(new CertificateImportParams {
            RawData = x509Certificate2.Export(X509ContentType.Pfx, password),
            Password = password
        });

        var certificates = await farm.CertificateList();
        Assert.AreEqual(2, certificates.Length);

        Assert.IsTrue(certificates.Any(x => dnsName1 == x.CommonName && !x.IsInToken));
        Assert.IsTrue(certificates.Any(x => dnsName2 == x.CommonName && x.IsInToken));
    }

    [TestMethod]
    public async Task Previous_certificates_should_not_have_auto_validate()
    {
        var dnsName1 = $"{Guid.NewGuid()}.com";
        var farm = await ServerFarmDom.Create();
        await farm.Update(new ServerFarmUpdateParams {
            AutoValidateCertificate = new PatchOfBoolean { Value = true },
            MaxCertificateCount = new PatchOfInteger { Value = 2 }
        });

        var certificates = await farm.CertificateList();
        Assert.IsTrue(certificates.First().AutoValidate);

        // create new
        await farm.CertificateReplace(new CertificateCreateParams {
            CertificateSigningRequest = new CertificateSigningRequest { CommonName = dnsName1 }
        });

        certificates = await farm.CertificateList();
        Assert.IsFalse(certificates[0].AutoValidate);
        Assert.IsFalse(certificates[1].AutoValidate);
        Assert.AreEqual(dnsName1, certificates[0].CommonName);
    }

    [TestMethod]
    public async Task Configure_should_return_all_certificates()
    {
        var dnsName1 = $"{Guid.NewGuid()}.com";
        var farm = await ServerFarmDom.Create();
        await farm.Update(new ServerFarmUpdateParams { MaxCertificateCount = new PatchOfInteger { Value = 3 } });

        await farm.CertificateReplace(new CertificateCreateParams {
            CertificateSigningRequest = new CertificateSigningRequest { CommonName = dnsName1 }
        });

        var dnsName2 = $"{Guid.NewGuid()}.com";
        await farm.CertificateReplace(new CertificateCreateParams {
            CertificateSigningRequest = new CertificateSigningRequest { CommonName = dnsName2 }
        });

        var dnsName3 = $"{Guid.NewGuid()}.com";
        await farm.CertificateReplace(new CertificateCreateParams {
            CertificateSigningRequest = new CertificateSigningRequest { CommonName = dnsName3 }
        });

        var dnsName4 = $"{Guid.NewGuid()}.com";
        await farm.CertificateReplace(new CertificateCreateParams {
            CertificateSigningRequest = new CertificateSigningRequest { CommonName = dnsName4 }
        });

        //-----------
        // check: get certificate by publicIp
        //-----------
        await farm.DefaultServer.Configure();
        Assert.AreEqual(3, farm.DefaultServer.ServerConfig.Certificates.Length);

        Assert.IsTrue(farm.DefaultServer.ServerConfig.Certificates.Any(x => {
            var certificate = new X509Certificate2(x.RawData);
            return dnsName2 == certificate.GetNameInfo(X509NameType.DnsName, false);
        }));

        Assert.IsTrue(farm.DefaultServer.ServerConfig.Certificates.Any(x => {
            var certificate = new X509Certificate2(x.RawData);
            return dnsName3 == certificate.GetNameInfo(X509NameType.DnsName, false);
        }));

        Assert.IsTrue(farm.DefaultServer.ServerConfig.Certificates.Any(x => {
            var certificate = new X509Certificate2(x.RawData);
            return dnsName4 == certificate.GetNameInfo(X509NameType.DnsName, false);
        }));
    }
}