using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;

namespace VpnHood.Test;

public class TestWebServer : IDisposable
{
    private readonly WebServer _webServer;
    public IPEndPoint[] HttpsEndPoints { get; } = {
        IPEndPoint.Parse("192.168.86.209:15001"),
        IPEndPoint.Parse("192.168.86.209:15002"),
        IPEndPoint.Parse("192.168.86.209:15003"),
        IPEndPoint.Parse("192.168.86.209:15004"),
    };

    public IPEndPoint[] HttpEndPoints { get; } = {
        IPEndPoint.Parse("192.168.86.209:15005"),
        IPEndPoint.Parse("192.168.86.209:15006"),
        IPEndPoint.Parse("192.168.86.209:15007"),
        IPEndPoint.Parse("192.168.86.209:15008"),
    };

    public Uri[] HttpUrls { get; } 
    public Uri[] HttpsUrls { get; }

    public string FileContent1;
    public string FileContent2;

    private TestWebServer()
    {
        HttpUrls = HttpEndPoints.Select(x => new Uri($"http://{x}/file1")).ToArray();
        HttpsUrls = HttpsEndPoints.Select(x => new Uri($"https://{x}/file1")).ToArray(); 

        // Init files
        FileContent1 = string.Empty;
        FileContent2 = string.Empty;
        for (var i = 0; i < 100; i++)
        {
            FileContent1 += Guid.NewGuid().ToString();
            FileContent2 += Guid.NewGuid().ToString();
        }

        // Create web server
        var webServerOptions = new WebServerOptions
        {
            Certificate = new X509Certificate2("Assets/VpnHood.UnitTest.pfx", (string?)null, X509KeyStorageFlags.Exportable),
            AutoRegisterCertificate= false,
            Mode = HttpListenerMode.EmbedIO
        };

        foreach (var endpoint in HttpEndPoints)
            webServerOptions.AddUrlPrefix($"http://{endpoint}");

        foreach (var endpoint in HttpsEndPoints)
            webServerOptions.AddUrlPrefix($"https://{endpoint}");

        _webServer = new WebServer(webServerOptions)
            .WithWebApi("/", c => c.WithController(() => new ApiController(this)));
    }

    public static TestWebServer Create()
    {
        TcpClient tcpClient = new TcpClient();
        tcpClient.Connect(IPEndPoint.Parse("8.8.8.8:443"));
        var a = tcpClient.Client.LocalEndPoint;

        var ret = new TestWebServer();
        ret._webServer.Start();
        return ret;
    }

    public void Dispose()
    {
        _webServer.Dispose();
    }

    private class ApiController : WebApiController
    {
        private readonly TestWebServer _testWebServer;

        public ApiController(TestWebServer testWebServer)
        {
            _testWebServer = testWebServer;
        }

        [Route(HttpVerbs.Get, "/file1")]
        public Task<string> File1()
        {
            return Task.FromResult(_testWebServer.FileContent1);
        }

        [Route(HttpVerbs.Get, "/file2")]
        public Task<string> File2()
        {
            return Task.FromResult(_testWebServer.FileContent1);
        }
    }
}