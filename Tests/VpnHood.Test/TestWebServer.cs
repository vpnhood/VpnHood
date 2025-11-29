using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using VpnHood.AppLib.WebServer.Helpers;
using VpnHood.Core.Toolkit.Utils;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;

namespace VpnHood.Test;

public class TestWebServer : IDisposable
{
    private readonly List<WebserverLite> _webServers = [];

    public IPEndPoint[] HttpsV4EndPoints { get; } = [
        IPEndPoint.Parse("127.10.1.1:15001"),
        IPEndPoint.Parse("127.10.1.1:15002"),
        IPEndPoint.Parse("127.10.1.1:15003"),
        IPEndPoint.Parse("127.10.1.1:15004")
    ];

    public IPEndPoint[] HttpV4EndPoints { get; } = [
        IPEndPoint.Parse("127.10.1.1:15005"),
        IPEndPoint.Parse("127.10.1.1:15006"),
        IPEndPoint.Parse("127.10.1.1:15007"),
        IPEndPoint.Parse("127.10.1.1:15008")
    ];

    public IPEndPoint[] UdpEndPoints { get; } = [
        IPEndPoint.Parse("127.10.1.1:20101"),
        IPEndPoint.Parse("127.10.1.1:20102"),
        IPEndPoint.Parse("127.10.1.1:20103"),
        IPEndPoint.Parse("[::1]:20101"),
        IPEndPoint.Parse("[::1]:20102"),
        IPEndPoint.Parse("[::1]:20103")
    ];

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
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private CancellationToken CancellationToken => _cancellationTokenSource.Token;

    private TestWebServer()
    {
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
        Console.WriteLine("started...");
        try {
            foreach (var webServer in _webServers)
                webServer.StartAsync(CancellationToken);
        }
        catch (Exception ex) {
            Console.WriteLine("sss");
            throw;
        }

        Console.WriteLine("starting upd...");
        StartUdpEchoServer();
        Console.WriteLine("stopping upd...");
        return Task.CompletedTask;

    }

    public static TestWebServer Create()
    {
        var ret = new TestWebServer();
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

        Console.WriteLine("disposed...");

        _cancellationTokenSource.Dispose();
    }

    private class ApiController(TestWebServer testWebServer) : ControllerBase
    {
        public override void AddRoutes(IRouteMapper mapper)
        {
            mapper.AddStatic(WatsonWebserver.Core.HttpMethod.GET, "/file1", async ctx => {
                await ctx.SendPlainText(testWebServer.FileContent1);
            });

            mapper.AddStatic(WatsonWebserver.Core.HttpMethod.GET, "/file2", async ctx => {
                await ctx.SendPlainText(testWebServer.FileContent2);
            });
        }
    }
}