using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.NetTester.Testers.HttpTesters;
using VpnHood.NetTester.Testers.QuicTesters;
using VpnHood.NetTester.Testers.TcpTesters;
using VpnHood.Server.Access;

namespace VpnHood.NetTester.Servers;

internal class ServerHost(IPAddress listenerIp) : IDisposable
{
    private QuicTesterServer? _quicTesterServer;
    private TcpTesterServer? _tcpTesterServer;
    private HttpTesterServer? _httpTesterServer;
    private CancellationTokenSource? _cancellationTokenSource;

    public async Task Configure(ServerConfig serverConfig)
    {
        // start server
        Stop();

        _cancellationTokenSource = new CancellationTokenSource();
        var certificate = await PrepareCertificate(serverConfig);

        // start with new config
        if (serverConfig.TcpPort != 0) {
            var tcpEndPoint = new IPEndPoint(listenerIp, serverConfig.TcpPort);
            _tcpTesterServer = new TcpTesterServer();
            _tcpTesterServer?.Start(tcpEndPoint, _cancellationTokenSource.Token);
        }

        // start http server
        var httpEndPoint = serverConfig.HttpPort != 0 ? new IPEndPoint(listenerIp, serverConfig.HttpPort) : null;
        var httpsEndPoint = serverConfig.HttpsPort != 0 ? new IPEndPoint(listenerIp, serverConfig.HttpsPort) : null;
        if (httpEndPoint != null || httpsEndPoint != null) {
            _httpTesterServer = new HttpTesterServer(
                httpEndPoint: httpEndPoint,
                httpsEndPoint: httpsEndPoint,
                certificate: certificate,
                _cancellationTokenSource.Token);
        }

        // start quic server
        var quicEndPoint = serverConfig.QuicPort != 0 ? new IPEndPoint(listenerIp, serverConfig.QuicPort) : null;
        if (quicEndPoint != null) {
            _quicTesterServer = new QuicTesterServer(quicEndPoint, certificate, _cancellationTokenSource.Token);
            _ = _quicTesterServer.Start();
        }
    }

    private static async Task<X509Certificate2> PrepareCertificate(ServerConfig serverConfig)
    {
        if (serverConfig.IsValidDomain) {
            if (string.IsNullOrWhiteSpace(serverConfig.Domain))
                throw new InvalidOperationException("Domain is required for a valid domain.");
            return LoadCertificate(serverConfig.Domain);
        }

        return await CreateCertificate(serverConfig.Domain);
    }

    private static X509Certificate2 LoadCertificate(string domain)
    {
        var raw = File.ReadAllBytes($"../certs/{domain}.pfx");
        var cert = X509CertificateLoader.LoadPkcs12(raw, password: null);
        return cert;
    }

    private static async Task<X509Certificate2> CreateCertificate(string domain)
    {
        try {
            // create 5 second cancellation token
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var originalCert = await CertificateUtil.GetCertificateFromUrl(new Uri($"https://{domain}"), cts.Token);
            var copyCert = CertificateUtil.CreateSelfSigned(originalCert);
            VhLogger.Instance.LogInformation("Created self-signed certificate from a url. Domain: {Domain}", domain);
            return copyCert;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Failed to create self-signed certificate from the url. Domain: {Domain}", domain);
        }

        var cert = CertificateUtil.CreateSelfSigned($"CN={domain}");
        VhLogger.Instance.LogInformation("Created self-signed certificate. Domain: {Domain}", domain);
        return cert;
    }

    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
        _tcpTesterServer?.Dispose();
        _httpTesterServer?.Dispose();
        _quicTesterServer?.Dispose();
    }

    public void Dispose()
    {
        Stop();
    }
}