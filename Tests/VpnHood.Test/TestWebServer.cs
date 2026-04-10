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

    private void StartTcpDataServer()
    {
        foreach (var endpoint in LocalEps.AllTcpDataEndPoints) {
            var listener = new TcpListener(endpoint);
            listener.Start();
            _tcpDataListeners.Add(listener);
            _ = AcceptTcpDataClients(listener);
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