using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.WebServer.Helpers;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Test.Providers;
using VpnHood.Test.QuicTesters;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;
using HttpMethod = WatsonWebserver.Core.HttpMethod;

namespace VpnHood.Test;

public class TestWebServer : IDisposable
{
    public TestIps TestIps { get; }
    public TestWebServerMockEps MockEps { get; }
    public TestWebServerLocalEps LocalEps { get; }

    public string FileContent1 { get; set; }
    public string FileContent2 { get; set; }

    private readonly List<WebserverLite> _webServers = [];
    private IReadOnlyList<UdpClient> UdpClients { get; }
    private IReadOnlyList<UdpClient> UdpUploadClients { get; }
    private IReadOnlyList<UdpClient> UdpDownloadClients { get; }
    private readonly List<QuicTesterServer> _quicServers = [];
    private readonly List<TcpListener> _tcpDataListeners = [];
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private CancellationToken CancellationToken => _cancellationTokenSource.Token;

    private TestWebServer(TestIps testIps)
    {
        TestIps = testIps;
        LocalEps = new TestWebServerLocalEps(testIps);
        MockEps = new TestWebServerMockEps(LocalEps, testIps);
        UdpClients = LocalEps.AllUdpEchoEndPoints.Select(x => new UdpClient(x)).ToArray();
        UdpUploadClients = LocalEps.AllUdpUploadEndPoints.Select(x => new UdpClient(x)).ToArray();
        UdpDownloadClients = LocalEps.AllUdpDownloadEndPoints.Select(x => new UdpClient(x)).ToArray();

        // Init files
        FileContent1 = string.Empty;
        FileContent2 = string.Empty;
        for (var i = 0; i < 100; i++) {
            FileContent1 += Guid.NewGuid().ToString();
            FileContent2 += Guid.NewGuid().ToString();
        }

        // Create web servers - one per HTTP endpoint
        foreach (var endpoint in LocalEps.AllHttpEndPoints) {
            var settings = new WebserverSettings(endpoint.Address.ToString(), endpoint.Port);
            var webServer = new WebserverLite(settings, DefaultRoute);
            webServer
                .AddRouteMapper(isDebugMode: true)
                .AddController(new ApiController(this));
            _webServers.Add(webServer);
        }

        foreach (var endpoint in LocalEps.AllHttpsEndPoints) {
            var settings = new WebserverSettings(endpoint.Address.ToString(), endpoint.Port) {
                Ssl = new WebserverSettings.SslSettings {
                    Enable = true,
                    PfxCertificateFile = "Assets/VpnHood.UnitTest.pfx"
                }
            };
            var webServer = new WebserverLite(settings, DefaultRoute);
            webServer
                .AddRouteMapper(isDebugMode: true)
                .AddController(new ApiController(this));
            _webServers.Add(webServer);
        }
    }

    public Task Start()
    {
        VhLogger.Instance.LogInformation(GeneralEventId.Test, "TestWebServer Started...");
        try {
            foreach (var webServer in _webServers)
                webServer.StartAsync(CancellationToken);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogInformation(GeneralEventId.Test, ex, "TestWebServer could not start");
            throw;
        }

        VhLogger.Instance.LogInformation(GeneralEventId.Test, "TestWebServer starting UDP...");
        StartUdpEchoServer();
        StartUdpUploadServer();
        StartUdpDownloadServer();

        VhLogger.Instance.LogInformation(GeneralEventId.Test, "TestWebServer starting QUIC...");
        StartQuicEchoServer();

        VhLogger.Instance.LogInformation(GeneralEventId.Test, "TestWebServer starting TcpData...");
        StartTcpDataServer();

        return Task.CompletedTask;
    }

    public static TestWebServer Create(TestIps filterIps)
    {
        var ret = new TestWebServer(filterIps);
        ret.Start();
        return ret;
    }

    private void StartUdpEchoServer()
    {
        foreach (var udpClient in UdpClients) {
            udpClient.Client.IOControl(-1744830452, [0], [0]);
            _ = StartUdpEchoServer(udpClient);
        }
    }

    private async Task StartUdpEchoServer(UdpClient udpClient)
    {
        while (!CancellationToken.IsCancellationRequested) {
            var udpResult = await udpClient.ReceiveAsync(CancellationToken);
            await udpClient.SendAsync(udpResult.Buffer, udpResult.RemoteEndPoint, CancellationToken);
        }
    }

    private void StartUdpUploadServer()
    {
        foreach (var udpClient in UdpUploadClients)
            _ = RunUdpUploadServer(udpClient);
    }

    private async Task RunUdpUploadServer(UdpClient udpClient)
    {
        while (!CancellationToken.IsCancellationRequested) {
            var result = await udpClient.ReceiveAsync(CancellationToken);
            var buf = result.Buffer;
            // First 4 bytes are the declared length (big-endian), rest is data
            var declaredLength = (int)((uint)buf[0] << 24 | (uint)buf[1] << 16 | (uint)buf[2] << 8 | buf[3]);
            uint checksum = 0;
            for (var i = 4; i < 4 + declaredLength && i < buf.Length; i++)
                checksum += buf[i];
            var response = new[] {
                (byte)(checksum >> 24), (byte)(checksum >> 16),
                (byte)(checksum >> 8), (byte)checksum
            };
            await udpClient.SendAsync(response, result.RemoteEndPoint, CancellationToken);
        }
    }

    private void StartUdpDownloadServer()
    {
        foreach (var udpClient in UdpDownloadClients)
            _ = RunUdpDownloadServer(udpClient);
    }

    private async Task RunUdpDownloadServer(UdpClient udpClient)
    {
        while (!CancellationToken.IsCancellationRequested) {
            var result = await udpClient.ReceiveAsync(CancellationToken);
            var buf = result.Buffer;
            var byteCount = (int)((uint)buf[0] << 24 | (uint)buf[1] << 16 | (uint)buf[2] << 8 | buf[3]);
            var data = new byte[byteCount];
            Random.Shared.NextBytes(data);
            uint checksum = 0;
            foreach (var b in data)
                checksum += b;
            // Response: data + 4-byte checksum
            var response = new byte[byteCount + 4];
            data.CopyTo(response, 0);
            response[byteCount] = (byte)(checksum >> 24);
            response[byteCount + 1] = (byte)(checksum >> 16);
            response[byteCount + 2] = (byte)(checksum >> 8);
            response[byteCount + 3] = (byte)checksum;
            await udpClient.SendAsync(response, result.RemoteEndPoint, CancellationToken);
        }
    }

    private void StartTcpDataServer()
    {
        foreach (var endpoint in LocalEps.AllTcpDataEndPoints) {
            var listener = new TcpListener(endpoint);
            listener.Start();
            _tcpDataListeners.Add(listener);
            _ = AcceptTcpDataClients(listener);
        }

        foreach (var endpoint in LocalEps.AllTcpUploadEndPoints) {
            var listener = new TcpListener(endpoint);
            listener.Start();
            _tcpDataListeners.Add(listener);
            _ = AcceptTcpUploadClients(listener);
        }

        foreach (var endpoint in LocalEps.AllTcpDownloadEndPoints) {
            var listener = new TcpListener(endpoint);
            listener.Start();
            _tcpDataListeners.Add(listener);
            _ = AcceptTcpDownloadClients(listener);
        }
    }

    private async Task AcceptTcpDataClients(TcpListener listener)
    {
        while (!CancellationToken.IsCancellationRequested) {
            var client = await listener.AcceptTcpClientAsync(CancellationToken);
            _ = SendTcpData(client);
        }
    }

    private static async Task SendTcpData(TcpClient client)
    {
        using var _ = client;
        var buffer = new byte[2000];
        Random.Shared.NextBytes(buffer);
        await client.GetStream().WriteAsync(buffer);
    }

    private async Task AcceptTcpUploadClients(TcpListener listener)
    {
        while (!CancellationToken.IsCancellationRequested) {
            var client = await listener.AcceptTcpClientAsync(CancellationToken);
            _ = HandleTcpUpload(client);
        }
    }

    private static async Task HandleTcpUpload(TcpClient client)
    {
        using var _ = client;
        var stream = client.GetStream();

        // Read 4-byte length prefix (big-endian)
        var lenBuf = new byte[4];
        await stream.ReadExactlyAsync(lenBuf);
        var byteCount = (int)(
            (uint)lenBuf[0] << 24 | (uint)lenBuf[1] << 16 |
            (uint)lenBuf[2] << 8 | lenBuf[3]);

        // Read exactly byteCount bytes and compute checksum
        var data = new byte[byteCount];
        await stream.ReadExactlyAsync(data);
        uint checksum = 0;
        foreach (var b in data)
            checksum += b;

        // Send back 4-byte checksum (big-endian)
        var result = new[] {
            (byte)(checksum >> 24), (byte)(checksum >> 16),
            (byte)(checksum >> 8), (byte)checksum
        };
        await stream.WriteAsync(result);
    }

    private async Task AcceptTcpDownloadClients(TcpListener listener)
    {
        while (!CancellationToken.IsCancellationRequested) {
            var client = await listener.AcceptTcpClientAsync(CancellationToken);
            _ = HandleTcpDownload(client);
        }
    }

    private static async Task HandleTcpDownload(TcpClient client)
    {
        using var _ = client;
        var stream = client.GetStream();

        // Read 4-byte length prefix (big-endian)
        var lenBuf = new byte[4];
        await stream.ReadExactlyAsync(lenBuf);
        var byteCount = (int)(
            (uint)lenBuf[0] << 24 | (uint)lenBuf[1] << 16 |
            (uint)lenBuf[2] << 8 | lenBuf[3]);

        // Generate random data and compute checksum
        var data = new byte[byteCount];
        Random.Shared.NextBytes(data);
        uint checksum = 0;
        foreach (var b in data)
            checksum += b;

        // Send data followed by 4-byte checksum (big-endian)
        await stream.WriteAsync(data);
        var result = new[] {
            (byte)(checksum >> 24), (byte)(checksum >> 16),
            (byte)(checksum >> 8), (byte)checksum
        };
        await stream.WriteAsync(result);
    }

    private void StartQuicEchoServer()
    {
        var certificate = X509CertificateLoader.LoadPkcs12FromFile("Assets/VpnHood.UnitTest.pfx", null, X509KeyStorageFlags.Exportable);
        foreach (var endpoint in LocalEps.AllQuicEndPoints) {
            var quicServer = new QuicTesterServer(endpoint, certificate, CancellationToken);
            _quicServers.Add(quicServer);
            _ = quicServer.Start();
        }
    }

    private static async Task DefaultRoute(HttpContextBase ctx)
    {
        ctx.Response.StatusCode = (int)HttpStatusCode.NotFound;
        await ctx.Response.Send();
    }

    public void Dispose()
    {
        _cancellationTokenSource.TryCancel();
        foreach (var webServer in _webServers)
            webServer.TryStop(); // dispose has issues if there are active connections

        foreach (var udpClient in UdpClients)
            udpClient.Dispose();

        foreach (var udpClient in UdpUploadClients)
            udpClient.Dispose();

        foreach (var udpClient in UdpDownloadClients)
            udpClient.Dispose();

        foreach (var quicServer in _quicServers)
            quicServer.Dispose();

        foreach (var listener in _tcpDataListeners)
            listener.Dispose();

        VhLogger.Instance.LogInformation(GeneralEventId.Test, "Test Server Disposed.");
        _cancellationTokenSource.Dispose();
    }

    private class ApiController(TestWebServer testWebServer) : ControllerBase
    {
        public override void AddRoutes(IRouteMapper mapper)
        {
            mapper.AddStatic(HttpMethod.GET, "/file1",
                async ctx => { await ctx.SendPlainText(testWebServer.FileContent1); });

            mapper.AddStatic(HttpMethod.GET, "/file2",
                async ctx => { await ctx.SendPlainText(testWebServer.FileContent2); });
        }
    }
}