using System.Diagnostics.CodeAnalysis;
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
    private readonly List<WebserverLite> _webServers = [];

    public IPEndPoint[] HttpsV4EndPoints { get; }
    public IPEndPoint[] HttpV4EndPoints { get; }
    public IPEndPoint[] UdpEndPoints { get; }
    public IPEndPoint[] QuicEndPoints { get; }

    public IPEndPoint QuicEndPoint1 => QuicEndPoints[0];
    public IPEndPoint QuicEndPoint2 => QuicEndPoints[1];
    public IPEndPoint HttpsV4RefusedEndPoint1 => new(HttpsV4EndPoint1.Address, 9999);
    public IPEndPoint HttpsV4EndPoint1 => HttpsV4EndPoints[0];
    public IPEndPoint HttpsV4EndPoint2 => HttpsV4EndPoints[1];
    public IPEndPoint HttpV4EndPoint1 => HttpV4EndPoints[0];
    public IPEndPoint HttpV4EndPoint2 => HttpV4EndPoints[1];
    public IPEndPoint UdpV4EndPoint1 => UdpV4EndPoints[0];
    public IPEndPoint UdpV4EndPoint2 => UdpV4EndPoints[1];
    public IPEndPoint UdpV6EndPoint1 => UdpV6EndPoints[0];
    public IPEndPoint UdpV6EndPoint2 => UdpV6EndPoints[1];

    public IPEndPoint[] UdpV4EndPoints =>
        UdpEndPoints.Where(x => x.AddressFamily == AddressFamily.InterNetwork).ToArray();

    public IPEndPoint[] UdpV6EndPoints =>
        UdpEndPoints.Where(x => x.AddressFamily == AddressFamily.InterNetworkV6).ToArray();


    public Uri[] HttpUrls { get; }
    public Uri[] HttpsUrls { get; }

    public string FileContent1 { get; set; }
    public string FileContent2 { get; set; }

    public Uri FileHttpUrl1 => new($"http://{HttpV4EndPoints.First()}/file1");

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public Uri FileHttpUrl2 => new($"http://{HttpV4EndPoints.First()}/file2");

    private UdpClient[] UdpClients { get; }
    private readonly List<QuicTesterServer> _quicServers = [];
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private CancellationToken CancellationToken => _cancellationTokenSource.Token;

    private TestWebServer(TestNetFilterIps filterIps)
    {
        HttpsV4EndPoints = [
            new IPEndPoint(filterIps.LocalTestIpV4, 15001),
            new IPEndPoint(filterIps.LocalTestIpV4, 15002),
            new IPEndPoint(filterIps.LocalTestIpV4, 15003),
            new IPEndPoint(filterIps.LocalTestIpV4, 15004)
        ];

        HttpV4EndPoints = [
            new IPEndPoint(filterIps.LocalTestIpV4, 15005),
            new IPEndPoint(filterIps.LocalTestIpV4, 15006),
            new IPEndPoint(filterIps.LocalTestIpV4, 15007),
            new IPEndPoint(filterIps.LocalTestIpV4, 15008)
        ];

        UdpEndPoints = [
            new IPEndPoint(filterIps.LocalTestIpV4, 20101),
            new IPEndPoint(filterIps.LocalTestIpV4, 20102),
            new IPEndPoint(filterIps.LocalTestIpV4, 20103),
            new IPEndPoint(filterIps.LocalTestIpV6, 20101),
            new IPEndPoint(filterIps.LocalTestIpV6, 20102),
            new IPEndPoint(filterIps.LocalTestIpV6, 20103)
        ];

        QuicEndPoints = [
            new IPEndPoint(filterIps.LocalTestIpV4, 25001),
            new IPEndPoint(filterIps.LocalTestIpV4, 25002)
        ];

        HttpUrls = HttpV4EndPoints.Select(x => new Uri($"http://{x}/file1")).ToArray();
        HttpsUrls = HttpsV4EndPoints.Select(x => new Uri($"https://{x}/file1")).ToArray();
        UdpClients = UdpEndPoints.Select(x => new UdpClient(x)).ToArray();

        // Init files
        FileContent1 = string.Empty;
        FileContent2 = string.Empty;
        for (var i = 0; i < 100; i++) {
            FileContent1 += Guid.NewGuid().ToString();
            FileContent2 += Guid.NewGuid().ToString();
        }

        // Create web servers - one per HTTP endpoint
        foreach (var endpoint in HttpV4EndPoints) {
            var settings = new WebserverSettings(endpoint.Address.ToString(), endpoint.Port);
            var webServer = new WebserverLite(settings, DefaultRoute);
            webServer
                .AddRouteMapper(isDebugMode: true)
                .AddController(new ApiController(this));
            _webServers.Add(webServer);
        }

        foreach (var endpoint in HttpsV4EndPoints) {
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
        VhLogger.Instance.LogInformation(GeneralEventId.Test, "TestWebServer started UDP...");

        VhLogger.Instance.LogInformation(GeneralEventId.Test, "TestWebServer starting QUIC...");
        StartQuicEchoServer();
        VhLogger.Instance.LogInformation(GeneralEventId.Test, "TestWebServer started QUIC...");
        return Task.CompletedTask;
    }

    public static TestWebServer Create(TestNetFilterIps filterIps)
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

    private void StartQuicEchoServer()
    {
        var certificate = X509CertificateLoader.LoadPkcs12FromFile("Assets/VpnHood.UnitTest.pfx", null, X509KeyStorageFlags.Exportable);
        foreach (var endpoint in QuicEndPoints) {
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