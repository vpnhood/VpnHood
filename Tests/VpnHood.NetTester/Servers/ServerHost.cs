using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.NetTester.Testers.HttpTesters;
using VpnHood.NetTester.Testers.TcpTesters;
using VpnHood.Server.Access;

namespace VpnHood.NetTester.Servers;

internal class ServerHost(IPAddress listenerIp) : IDisposable
{
    private TcpTesterServer? _tcpTesterServer;
    private HttpTesterServer? _httpTesterServer;
    private CancellationTokenSource? _cancellationTokenSource;

    public async Task Configure(ServerConfig serverConfig)
    {
        // start server
        Stop();

        _cancellationTokenSource = new CancellationTokenSource();

        // start with new config
        if (serverConfig.TcpPort != 0) {
            var tcpEndPoint = new IPEndPoint(listenerIp, serverConfig.TcpPort);
            _tcpTesterServer = new TcpTesterServer();
            _tcpTesterServer?.Start(tcpEndPoint, _cancellationTokenSource.Token);
        }

        var httpEndPoint = serverConfig.HttpPort!=0 ? new IPEndPoint(listenerIp, serverConfig.HttpPort) : null;
        var httpsEndPoint = serverConfig.HttpsPort!=0 ? new IPEndPoint(listenerIp, serverConfig.HttpsPort) : null;
        if (httpEndPoint !=null || httpsEndPoint!=null) {
            _httpTesterServer = new HttpTesterServer(
                httpEndPoint: httpEndPoint, 
                httpsEndPoint: httpsEndPoint,
                certificate2: serverConfig.HttpsPort != 0 ? await CreateCertificate(serverConfig.HttpsDomain) : null,
                _cancellationTokenSource.Token);
        }
    }

    private static async Task<X509Certificate2> CreateCertificate(string? domain)
    {
        if (domain != null) {
            try {
                // create 5 second cancellation token
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var originalCert = await CertificateUtil.GetCertificateFromUrl(new Uri($"https://{domain}"), cts.Token);
                var ret =  CertificateUtil.CreateSelfSigned(originalCert);
                VhLogger.Instance.LogInformation("Created self-signed certificate from a url. Domain: {Domain}", domain);
                return ret;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Failed to create self-signed certificate from the url. Domain: {Domain}", domain);
            }
        }

        domain ??= CertificateUtil.CreateRandomDns();
        var ret2 = CertificateUtil.CreateSelfSigned($"CN={domain}");
        VhLogger.Instance.LogInformation("Created self-signed certificate. Domain: {Domain}", domain);
        return ret2;
    }

    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
        _tcpTesterServer?.Dispose();
        _httpTesterServer?.Dispose();
    }

    public void Dispose()
    {
        Stop();
    }
}