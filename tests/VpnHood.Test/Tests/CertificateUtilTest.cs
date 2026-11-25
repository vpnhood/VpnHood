using System.Formats.Asn1;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using VpnHood.Core.Server;
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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CreateCopy_takes_every_field_but_the_key_and_the_signature(bool isEcdsa)
    {
        using var authority = new TestAuthority(isEcdsa);
        using var site = authority.CreateSiteCertificate("site.example", new Uri("http://127.0.0.1:1/authority.cer"));

        using var copy = CertificateUtil.CreateCopy(site);

        CollectionAssert.AreEqual(site.SubjectName.RawData, copy.SubjectName.RawData);
        CollectionAssert.AreEqual(site.IssuerName.RawData, copy.IssuerName.RawData);
        CollectionAssert.AreEqual(site.SerialNumberBytes.ToArray(), copy.SerialNumberBytes.ToArray());
        Assert.AreEqual(site.NotBefore, copy.NotBefore);
        Assert.AreEqual(site.NotAfter, copy.NotAfter);
        CollectionAssert.AreEqual(DescribeExtensions(site), DescribeExtensions(copy));
        Assert.AreEqual(site.SignatureAlgorithm.Value, copy.SignatureAlgorithm.Value);
        Assert.AreEqual(site.PublicKey.Oid.Value, copy.PublicKey.Oid.Value);
        CollectionAssert.AreEqual(site.PublicKey.EncodedParameters?.RawData, copy.PublicKey.EncodedParameters?.RawData);
        Assert.AreEqual(GetKeySize(site), GetKeySize(copy));
        CollectionAssert.AreNotEqual(site.PublicKey.EncodedKeyValue.RawData, copy.PublicKey.EncodedKeyValue.RawData);
        Assert.IsTrue(copy.HasPrivateKey);

        // the authority signed the original but not its copy
        Assert.IsTrue(authority.Verify(site));
        Assert.IsFalse(authority.Verify(copy));
    }

    [TestMethod]
    public void CreateCopy_ends_when_asked()
    {
        using var authority = new TestAuthority(isEcdsa: false);
        using var site = authority.CreateSiteCertificate("site.example", new Uri("http://127.0.0.1:1/authority.cer"));
        var notAfter = new DateTimeOffset(2031, 5, 6, 7, 8, 9, TimeSpan.Zero);

        using var copy = CertificateUtil.CreateCopy(site, notAfter);

        Assert.AreEqual(notAfter, new DateTimeOffset(copy.NotAfter));
        Assert.AreEqual(site.NotBefore, copy.NotBefore);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            CertificateUtil.CreateCopy(site, new DateTimeOffset(site.NotBefore).AddSeconds(-1)));
    }

    // the copy names the site's authority, which did not sign it; the server finds the authority at the address the
    // copy keeps and sends it along, as the site does
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_server_sends_the_sites_authority_with_its_copy(bool isEcdsa)
    {
        using var authority = new TestAuthority(isEcdsa);
        using var authorityServer = new AuthorityServer(authority.Certificate.RawData);
        using var site = authority.CreateSiteCertificate("site.example", authorityServer.Url);
        using var copy = CertificateUtil.CreateCopy(site);
        var certificateHostName = new CertificateHostName(copy);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        var serving = ServeTls(listener, certificateHostName.CertificateContext);
        var sentThumbprints = await GetSentThumbprints((IPEndPoint)listener.LocalEndpoint, "site.example");
        await serving;

        CollectionAssert.Contains(sentThumbprints.ToArray(), authority.Certificate.Thumbprint);
    }

    [TestMethod]
    public async Task GetCertificateFromEndPoint_asks_the_address_for_the_name()
    {
        using var certificate = CertificateUtil.CreateSelfSigned("CN=site.example");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var serving = ServeTls(listener, certificate);

        using var presented = await CertificateUtil.GetCertificateFromEndPoint(
            (IPEndPoint)listener.LocalEndpoint, "site.example", TestCt);

        Assert.AreEqual("site.example", await serving);
        Assert.AreEqual(certificate.Thumbprint, presented.Thumbprint);
    }

    // the name the client asked for
    private async Task<string> ServeTls(TcpListener listener, X509Certificate2 certificate)
    {
        using var tcpClient = await listener.AcceptTcpClientAsync(TestCt);
        await using var sslStream = new SslStream(tcpClient.GetStream());
        await sslStream.AuthenticateAsServerAsync(
            new SslServerAuthenticationOptions { ServerCertificate = certificate }, TestCt);

        return sslStream.TargetHostName;
    }

    private async Task ServeTls(TcpListener listener, SslStreamCertificateContext certificateContext)
    {
        using var tcpClient = await listener.AcceptTcpClientAsync(TestCt);
        await using var sslStream = new SslStream(tcpClient.GetStream());
        await sslStream.AuthenticateAsServerAsync(
            new SslServerAuthenticationOptions { ServerCertificateContext = certificateContext }, TestCt);
    }

    // what the server sent beside its own certificate: the client downloads none itself
    private async Task<IReadOnlyList<string>> GetSentThumbprints(IPEndPoint serverEndPoint, string hostName)
    {
        var thumbprints = new List<string>();
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(serverEndPoint, TestCt);
        await using var sslStream = new SslStream(tcpClient.GetStream());
        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
            TargetHost = hostName,
            CertificateChainPolicy = new X509ChainPolicy {
                DisableCertificateDownloads = true,
                RevocationMode = X509RevocationMode.NoCheck
            },
            RemoteCertificateValidationCallback = (_, _, chain, _) => {
                if (chain != null)
                    thumbprints.AddRange(chain.ChainPolicy.ExtraStore.Select(x => x.Thumbprint));

                return true;
            }
        }, TestCt);

        return thumbprints;
    }

    private static string[] DescribeExtensions(X509Certificate2 certificate) =>
        certificate.Extensions
            .Select(x => $"{x.Oid?.Value} {x.Critical} {Convert.ToHexString(x.RawData)}")
            .ToArray();

    private static int GetKeySize(X509Certificate2 certificate)
    {
        using var rsa = certificate.GetRSAPublicKey();
        using var ecdsa = certificate.GetECDsaPublicKey();
        return rsa?.KeySize ?? ecdsa?.KeySize ?? throw new NotSupportedException("The key is neither RSA nor ECDSA.");
    }

    // A made-up authority under a made-up root, as a public one is: RSA keys signing with SHA-256, or ECDSA P-384 keys
    // signing with SHA-384. Each has names of its own: Windows keeps the authorities it downloads, and where none
    // signed a certificate, it may send any one that bears the name the certificate gives
    private sealed class TestAuthority : IDisposable
    {
        private readonly bool _isEcdsa;
        private readonly AsymmetricAlgorithm _key;
        private readonly X509Certificate2 _root;

        public X509Certificate2 Certificate { get; }

        public TestAuthority(bool isEcdsa)
        {
            _isEcdsa = isEcdsa;
            var now = DateTimeOffset.UtcNow;
            var id = Guid.NewGuid().ToString("N");
            using var rootKey = CreateKey(isEcdsa ? 384 : 2048);
            var rootRequest = CreateAuthorityRequest($"CN=VpnHood Test Root {id}, O=VpnHood Test, C=US", rootKey);
            _root = rootRequest.Create(rootRequest.SubjectName, CreateGenerator(rootKey), now.AddYears(-5),
                now.AddYears(10), RandomNumberGenerator.GetBytes(16));

            _key = CreateKey(isEcdsa ? 384 : 2048);
            var request = CreateAuthorityRequest($"CN=VpnHood Test Authority {id}, O=VpnHood Test, C=US", _key);
            request.CertificateExtensions.Add(
                X509AuthorityKeyIdentifierExtension.CreateFromCertificate(_root, true, false));

            Certificate = request.Create(_root.SubjectName, CreateGenerator(rootKey), now.AddYears(-1), now.AddYears(3),
                RandomNumberGenerator.GetBytes(16));
        }

        // a site's certificate with the fields a public one has, its serial's first bit set as half of them are
        public X509Certificate2 CreateSiteCertificate(string dnsName, Uri authorityUrl)
        {
            var now = DateTimeOffset.UtcNow;
            using var key = CreateKey(_isEcdsa ? 256 : 2048);
            var request = CreateRequest($"CN={dnsName}", key);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));

            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            request.CertificateExtensions.Add(
                X509AuthorityKeyIdentifierExtension.CreateFromCertificate(Certificate, true, false));

            request.CertificateExtensions.Add(
                new X509AuthorityInformationAccessExtension(ocspUris: null, caIssuersUris: [authorityUrl.ToString()]));

            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName(dnsName);
            names.AddDnsName($"www.{dnsName}");
            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509Extension("2.5.29.32", EncodeDomainValidatedPolicy(), false));
            request.CertificateExtensions.Add(new X509Extension("1.3.6.1.4.1.11129.2.4.2", EncodeOctets(120), false));

            var serialNumber = RandomNumberGenerator.GetBytes(16);
            serialNumber[0] |= 0x80;
            return request.Create(Certificate.SubjectName, CreateGenerator(_key), now.AddDays(-1), now.AddDays(89),
                serialNumber);
        }

        // whether a certificate's chain to the root holds, every signature checked
        public bool Verify(X509Certificate2 certificate)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(_root);
            chain.ChainPolicy.ExtraStore.Add(Certificate);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.DisableCertificateDownloads = true;
            return chain.Build(certificate);
        }

        private CertificateRequest CreateRequest(string subjectName, AsymmetricAlgorithm key) =>
            new(new X500DistinguishedName(subjectName), new PublicKey(key),
                _isEcdsa ? HashAlgorithmName.SHA384 : HashAlgorithmName.SHA256);

        private CertificateRequest CreateAuthorityRequest(string subjectName, AsymmetricAlgorithm key)
        {
            var request = CreateRequest(subjectName, key);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, true));

            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            return request;
        }

        private AsymmetricAlgorithm CreateKey(int size) => _isEcdsa
            ? ECDsa.Create(size == 384 ? ECCurve.NamedCurves.nistP384 : ECCurve.NamedCurves.nistP256)
            : RSA.Create(size);

        private static X509SignatureGenerator CreateGenerator(AsymmetricAlgorithm key) => key is RSA rsa
            ? X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1)
            : X509SignatureGenerator.CreateForECDsa((ECDsa)key);

        // the CA/Browser Forum's domain-validated policy
        private static byte[] EncodeDomainValidatedPolicy()
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();
            writer.PushSequence();
            writer.WriteObjectIdentifier("2.23.140.1.2.1");
            writer.PopSequence();
            writer.PopSequence();
            return writer.Encode();
        }

        // bytes in place of the logs' signed timestamps, which only their logs can make
        private static byte[] EncodeOctets(int length)
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            writer.WriteOctetString(RandomNumberGenerator.GetBytes(length));
            return writer.Encode();
        }

        public void Dispose()
        {
            Certificate.Dispose();
            _root.Dispose();
            _key.Dispose();
        }
    }

    // The address an authority's certificate is downloaded from: it answers every request with that certificate
    private sealed class AuthorityServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly byte[] _certificate;

        public AuthorityServer(byte[] certificate)
        {
            _certificate = certificate;
            _listener.Start();
            _ = Serve(_cancellationTokenSource.Token);
        }

        public Uri Url => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/authority.cer");

        private async Task Serve(CancellationToken cancellationToken)
        {
            try {
                while (true) {
                    using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                    await Answer(client, cancellationToken);
                }
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested) {
                // the server's close
            }
        }

        private async Task Answer(TcpClient client, CancellationToken cancellationToken)
        {
            try {
                await using var stream = client.GetStream();
                await ReadRequest(stream, cancellationToken);
                var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/pkix-cert\r\n" +
                                                     $"Content-Length: {_certificate.Length}\r\nConnection: close\r\n\r\n");

                await stream.WriteAsync(header, cancellationToken);
                await stream.WriteAsync(_certificate, cancellationToken);
            }
            catch (IOException) {
                // the downloader left early
            }
        }

        private static async Task ReadRequest(Stream stream, CancellationToken cancellationToken)
        {
            var request = new List<byte>();
            var buffer = new byte[1024];
            while (!Encoding.ASCII.GetString(request.ToArray()).Contains("\r\n\r\n")) {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    return;

                request.AddRange(buffer.AsSpan(0, read));
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _listener.Stop();
            _cancellationTokenSource.Dispose();
        }
    }
}
