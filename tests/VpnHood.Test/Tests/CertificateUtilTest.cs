using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Server.Access;

namespace VpnHood.Test.Tests;

[TestClass]
public class CertificateUtilTest : TestBase
{
    [TestMethod]
    public async Task GetCertificateFromDomain_gets_the_certificate_the_domain_presents()
    {
        using var certificate = CertificateUtil.CreateSelfSigned("CN=site.example");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var serving = ServeTls(listener, certificate);

        using var presented = await CertificateUtil.GetCertificateFromDomain(
            IPAddress.Loopback.ToString(), ((IPEndPoint)listener.LocalEndpoint).Port, TestCt);

        await serving;
        Assert.AreEqual(certificate.Thumbprint, presented.Thumbprint);
    }

    // a failed connection threw a bare Exception, which hid the connection's own error
    [TestMethod]
    public async Task GetCertificateFromDomain_throws_the_connections_own_error()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        await Assert.ThrowsExactlyAsync<SocketException>(() =>
            CertificateUtil.GetCertificateFromDomain(IPAddress.Loopback.ToString(), port, TestCt));
    }

    // a cancel threw a bare Exception, as every error of the request was swallowed
    [TestMethod]
    public async Task GetCertificateFromDomain_ends_on_a_cancel_as_a_cancel()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CertificateUtil.GetCertificateFromDomain("localhost", cancellationToken: cancellationTokenSource.Token));
    }

    // its made-up authority ended on 8 March 2027, so the 5 years of its default certificate threw
    [TestMethod]
    public void CreateChained_makes_its_default_certificate()
    {
        using var rsa = RSA.Create();
        using var certificate = CertificateUtil.CreateChained(rsa);

        StringAssert.Contains(certificate.Issuer, "DigiCert SHA2 Secure Server CA");
        Assert.IsTrue(certificate.NotAfter > DateTime.Now.AddYears(4));
        Assert.IsTrue(certificate.HasPrivateKey);
    }

    private async Task ServeTls(TcpListener listener, X509Certificate2 certificate)
    {
        using var tcpClient = await listener.AcceptTcpClientAsync(TestCt);
        await using var sslStream = new SslStream(tcpClient.GetStream());
        await sslStream.AuthenticateAsServerAsync(
            new SslServerAuthenticationOptions { ServerCertificate = certificate }, TestCt);
    }
}
