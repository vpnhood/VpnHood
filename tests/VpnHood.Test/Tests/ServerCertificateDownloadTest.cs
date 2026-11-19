using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Client;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Server.Access;
using VpnHood.Net.Quic.MsQuic;
using VpnHood.Test.Providers;
using QuicClientConnectOptions = VpnHood.Net.Quic.Abstractions.QuicClientConnectOptions;

namespace VpnHood.Test.Tests;

// A server certificate that comes without the one that issued it: the VPN connection downloaded that one from the
// address the certificate names, which a certificate that someone on the way shows, a proxy's too, can make any
// address, fetched outside the tunnel. A Windows server looks for the issuer itself at its first connection, so a
// connection that downloads nothing goes first, and the count is the one after it
[TestClass]
public class ServerCertificateDownloadTest : TestBase
{
    [TestMethod]
    public async Task The_TCP_connection_downloads_no_certificate_for_the_servers_chain()
    {
        using var issuerAddress = new ConnectionCounter();
        using var certificate = CreateCertificateWithMissingIssuer(issuerAddress.Url);
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var serverEndPoint = (IPEndPoint)server.LocalEndpoint;

        var firstServing = ServeTls(server, certificate, TestCt);
        await using var firstClient = await ConnectTlsWithoutDownloads(serverEndPoint, TestCt);
        await using var firstServerStream = await firstServing;
        var serverLookCount = issuerAddress.ConnectionCount;

        bool? isDownloadDisabled = null;
        var serving = ServeTls(server, certificate, TestCt);
        var vpnEndPoint = new VpnEndPoint(serverEndPoint, "localhost", CertificateHash: null, PathBase: null);
        using var factory = new TcpStreamConnectionFactory(new TestSocketFactory(), proxyConnector: null, vpnEndPoint,
            (_, _, chain, _) => {
                isDownloadDisabled = chain?.ChainPolicy.DisableCertificateDownloads;
                return true;
            }, tcpPacketChannelKernelBufferSize: null);

        await using var connection = await factory.CreateConnection("download-test", isTcpPacketChannel: false,
            onConnectAttempt: null, TestCt);

        await using var serverStream = await serving;
        Assert.AreEqual(serverLookCount, issuerAddress.ConnectionCount,
            "The connection fetched the address its certificate names.");

        Assert.IsTrue(isDownloadDisabled, "The connection's chain may download a certificate.");
    }

    [TestMethod]
    public async Task The_QUIC_connection_downloads_no_certificate_for_the_servers_chain()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
            Assert.Inconclusive("QUIC is not supported on this platform.");

        using var issuerAddress = new ConnectionCounter();
        using var certificate = CreateCertificateWithMissingIssuer(issuerAddress.Url);
        await using var listener = await ListenQuic(certificate, TestCt);

        var firstAccepting = listener.AcceptConnectionAsync(TestCt);
        await using var firstClient = await QuicConnection.ConnectAsync(new QuicClientConnectionOptions {
            RemoteEndPoint = listener.LocalEndPoint,
            DefaultStreamErrorCode = 0,
            DefaultCloseErrorCode = 0,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions {
                TargetHost = "localhost",
                ApplicationProtocols = [SslApplicationProtocol.Http3],
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
                CertificateChainPolicy = CreatePolicyWithoutDownloads()
            }
        }, TestCt);

        await using var firstServerConnection = await firstAccepting;
        var serverLookCount = issuerAddress.ConnectionCount;

        // the client's own QUIC path, which asks the platform's QUIC client for no download
        bool? isDownloadDisabled = null;
        var accepting = listener.AcceptConnectionAsync(TestCt);
        var vpnEndPoint = new VpnEndPoint(listener.LocalEndPoint, "localhost", CertificateHash: null, PathBase: null);
        await using var factory = new QuicStreamConnectionFactory(new MsQuicClient(), vpnEndPoint,
            (_, _, chain, _) => {
                isDownloadDisabled = chain?.ChainPolicy.DisableCertificateDownloads;
                return true;
            }) { QuicEndPoint = listener.LocalEndPoint };

        await using var connection = await factory.CreateConnection("download-test", TestCt);
        await using var serverConnection = await accepting;
        Assert.AreEqual(serverLookCount, issuerAddress.ConnectionCount,
            "The connection fetched the address its certificate names.");

        Assert.IsTrue(isDownloadDisabled, "The connection's chain may download a certificate.");
    }

    // The QUIC client is a library for any app: where its caller asks nothing, the downloads are the platform's
    [TestMethod]
    public async Task The_QUIC_client_leaves_the_chains_downloads_to_the_platform_by_default()
    {
        if (!QuicListener.IsSupported || !QuicConnection.IsSupported)
            Assert.Inconclusive("QUIC is not supported on this platform.");

        using var certificate = CertificateUtil.CreateSelfSigned("CN=localhost");
        await using var listener = await ListenQuic(certificate, TestCt);

        bool? isDownloadDisabled = null;
        var accepting = listener.AcceptConnectionAsync(TestCt);
        await using var connection = await new MsQuicClient().ConnectAsync(new QuicClientConnectOptions {
            RemoteEndPoint = listener.LocalEndPoint,
            TargetHost = "localhost",
            CertificateValidationCallback = (_, _, chain, _) => {
                isDownloadDisabled = chain?.ChainPolicy.DisableCertificateDownloads;
                return true;
            }
        }, TestCt);

        await using var serverConnection = await accepting;
        Assert.AreEqual(false, isDownloadDisabled, "The client turned the chain's downloads off unasked.");
    }

    // A chain without its issuer is taken with the access key's certificate hash alone, now that the connection
    // downloads nothing: the way to reach a server behind a reverse proxy given its certificate without the chain
    [TestMethod]
    public async Task A_server_whose_chain_lacks_its_issuer_is_taken_with_the_keys_hash_alone()
    {
        using var issuerAddress = new ConnectionCounter();
        using var certificate = CreateCertificateWithMissingIssuer(issuerAddress.Url);
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var vpnEndPoint = new VpnEndPoint((IPEndPoint)server.LocalEndpoint, "localhost", CertificateHash: null,
            PathBase: null);

        var refusedServing = ServeTls(server, certificate, TestCt);
        using var refusingFactory = new TcpStreamConnectionFactory(new TestSocketFactory(), proxyConnector: null,
            vpnEndPoint, (_, serverCertificate, chain, errors) =>
                ConnectorService.IsServerCertificateValid(ReadOnlySpan<byte>.Empty, serverCertificate, chain, errors),
            tcpPacketChannelKernelBufferSize: null);

        await Assert.ThrowsAsync<AuthenticationException>(() => refusingFactory.CreateConnection("no-hash",
            isTcpPacketChannel: false, onConnectAttempt: null, TestCt));

        await ObserveServing(refusedServing);

        // a hash of another certificate is refused too
        using var otherCertificate = CertificateUtil.CreateSelfSigned();
        var otherHash = otherCertificate.GetCertHash();
        var otherServing = ServeTls(server, certificate, TestCt);
        using var otherHashFactory = new TcpStreamConnectionFactory(new TestSocketFactory(), proxyConnector: null,
            vpnEndPoint, (_, serverCertificate, chain, errors) =>
                ConnectorService.IsServerCertificateValid(otherHash, serverCertificate, chain, errors),
            tcpPacketChannelKernelBufferSize: null);

        await Assert.ThrowsAsync<AuthenticationException>(() => otherHashFactory.CreateConnection("other-hash",
            isTcpPacketChannel: false, onConnectAttempt: null, TestCt));

        await ObserveServing(otherServing);

        var certificateHash = certificate.GetCertHash();
        var serving = ServeTls(server, certificate, TestCt);
        using var factory = new TcpStreamConnectionFactory(new TestSocketFactory(), proxyConnector: null, vpnEndPoint,
            (_, serverCertificate, chain, errors) =>
                ConnectorService.IsServerCertificateValid(certificateHash, serverCertificate, chain, errors),
            tcpPacketChannelKernelBufferSize: null);

        await using var connection = await factory.CreateConnection("hash", isTcpPacketChannel: false,
            onConnectAttempt: null, TestCt);

        await using var serverStream = await serving;
    }

    // The server's side of a refused handshake may end either way: TLS 1.3 lets it finish before the client's refusal
    private static async Task ObserveServing(Task<SslStream> serving)
    {
        try {
            await using var serverStream = await serving;
        }
        catch (Exception ex) when (ex is IOException or AuthenticationException) {
            // it ended at the client's refusal
        }
    }

    // The server sends its certificate alone, which leaves the one that issued it for the client to find
    private static async Task<SslStream> ServeTls(TcpListener server, X509Certificate2 certificate,
        CancellationToken cancellationToken)
    {
        var client = await server.AcceptTcpClientAsync(cancellationToken);
        var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
            ApplicationProtocols = [SslApplicationProtocol.Http11],
            ServerCertificateContext =
                SslStreamCertificateContext.Create(certificate, additionalCertificates: [], offline: true)
        }, cancellationToken);

        return sslStream;
    }

    // A QUIC server that sends its certificate alone, as ServeTls does over TCP
    private static ValueTask<QuicListener> ListenQuic(X509Certificate2 certificate,
        CancellationToken cancellationToken) =>
        QuicListener.ListenAsync(new QuicListenerOptions {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            ApplicationProtocols = [SslApplicationProtocol.Http3],
            ConnectionOptionsCallback = (_, _, _) => ValueTask.FromResult(new QuicServerConnectionOptions {
                DefaultStreamErrorCode = 0,
                DefaultCloseErrorCode = 0,
                ServerAuthenticationOptions = new SslServerAuthenticationOptions {
                    ApplicationProtocols = [SslApplicationProtocol.Http3],
                    ServerCertificateContext =
                        SslStreamCertificateContext.Create(certificate, additionalCertificates: [], offline: true)
                }
            })
        }, cancellationToken);

    private static async Task<SslStream> ConnectTlsWithoutDownloads(IPEndPoint serverEndPoint,
        CancellationToken cancellationToken)
    {
        var client = new TcpClient(serverEndPoint.AddressFamily);
        await client.ConnectAsync(serverEndPoint, cancellationToken);
        var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
            TargetHost = "localhost",
            RemoteCertificateValidationCallback = (_, _, _, _) => true,
            CertificateChainPolicy = CreatePolicyWithoutDownloads()
        }, cancellationToken);

        return sslStream;
    }

    private static X509ChainPolicy CreatePolicyWithoutDownloads() =>
        new() { DisableCertificateDownloads = true, RevocationMode = X509RevocationMode.NoCheck };

    // A certificate for localhost under a made-up issuer and root, which names an address to download its issuer from
    private static X509Certificate2 CreateCertificateWithMissingIssuer(Uri issuerUrl)
    {
        var now = DateTimeOffset.UtcNow;
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootRequest = new CertificateRequest("CN=VpnHood Test Root", rootKey, HashAlgorithmName.SHA256);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var root = rootRequest.CreateSelfSigned(now.AddDays(-1), now.AddDays(30));

        using var issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuerRequest = new CertificateRequest("CN=VpnHood Test Issuer", issuerKey, HashAlgorithmName.SHA256);
        issuerRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        issuerRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(issuerRequest.PublicKey, false));
        using var issuerCertificate = issuerRequest.Create(root, now.AddDays(-1), now.AddDays(20), [1]);
        using var issuer = issuerCertificate.CopyWithPrivateKey(issuerKey);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer,
            includeKeyIdentifier: true, includeIssuerAndSerial: false));

        request.CertificateExtensions.Add(new X509AuthorityInformationAccessExtension(ocspUris: null,
            caIssuersUris: [issuerUrl.ToString()]));

        using var certificate = request.Create(issuer, now.AddDays(-1), now.AddDays(10), [2]);
        using var certificateWithKey = certificate.CopyWithPrivateKey(key);
        return CertificateUtil.CreateExportable(certificateWithKey);
    }

    // The address of the issuer: it counts the connections made to it and closes each, so a download ends at once
    private sealed class ConnectionCounter : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private int _connectionCount;

        public ConnectionCounter()
        {
            _listener.Start();
            _ = Accept(_cts.Token);
        }

        public int ConnectionCount => Volatile.Read(ref _connectionCount);
        public Uri Url => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/issuer.cer");

        private async Task Accept(CancellationToken cancellationToken)
        {
            try {
                while (true) {
                    using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                    Interlocked.Increment(ref _connectionCount);
                }
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested) {
                // the counter's close
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }
    }
}
