using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using VpnHood.NetTester.Streams;

namespace VpnHood.NetTester.Testers.HttpTesters;

internal class HttpTesterServer : IDisposable
{
    private readonly WebServer _webServer;

    public HttpTesterServer(
        IPEndPoint? httpEndPoint, 
        IPEndPoint? httpsEndPoint, 
        X509Certificate2? certificate, 
        CancellationToken cancellationToken)
    {
        // create web server
        var webServerOptions = new WebServerOptions {
            Certificate = certificate,
            AutoRegisterCertificate = false
        };

        if (httpEndPoint != null) {
            webServerOptions.AddUrlPrefix($"http://+:{httpEndPoint.Port}");

        }
        if (httpsEndPoint != null) {
            webServerOptions.AddUrlPrefix($"https://+:{httpsEndPoint.Port}");
        }

        _webServer = new WebServer(webServerOptions)
            .WithWebApi("/", c => c.WithController(() => new TestController()));

        _webServer.Start(cancellationToken);
    }

    public void Dispose()
    {
        _webServer.Dispose();
    }

    internal class TestController : WebApiController
    {
        [Route(HttpVerbs.Post, "/upload")]
        public async Task Upload()
        {
            await using var requestStream = HttpContext.OpenRequestStream();
            await using var streamDiscarder = new StreamDiscarder(null);
            await requestStream.CopyToAsync(streamDiscarder);
            await HttpContext.SendStringAsync("File uploaded successfully", "text/plain", Encoding.UTF8);
        }

        [Route(HttpVerbs.Get, "/download")]
        public async Task Download([QueryField] int length)
        {
            // read length from query string parameter
            await using var responseStream = HttpContext.OpenResponseStream();
            await using var randomReader = new StreamRandomReader(length, null);
            await randomReader.CopyToAsync(responseStream);
        }
    }
}