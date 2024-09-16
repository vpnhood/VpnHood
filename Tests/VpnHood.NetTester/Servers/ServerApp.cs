using System.Net;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;

namespace VpnHood.NetTester.Servers;

internal class ServerApp : IDisposable
{
    private readonly WebServer _webServer;
    private ServerHost ServerHost { get; }

    private ServerApp(IPEndPoint ipEndPoint)
    {
        ServerHost = new ServerHost(ipEndPoint.Address);

        // create web server
        var webServerOptions = new WebServerOptions();
        webServerOptions.AddUrlPrefix($"http://{ipEndPoint}");

        _webServer = new WebServer(webServerOptions)
            .WithWebApi("/", c => c.WithController(() => new CommandApiController(ServerHost)));
        //Logger.UnregisterLogger<ConsoleLogger>();

        _webServer.Start();
    }

    public static ServerApp Create(IPEndPoint ipEndPoint)
    {
        var ret = new ServerApp(ipEndPoint);
        return ret;
    }

    public void Dispose()
    {
        _webServer.Dispose();
    }

    internal class CommandApiController(ServerHost serverApp) : WebApiController
    {
        // ReSharper disable once UnusedMember.Local
        [Route(HttpVerbs.Post, "/config")]
        public async Task Configure()
        {
            var serverConfig = await HttpContext.GetRequestDataAsync<ServerConfig>();
            await serverApp.Configure(serverConfig);
        }
    }
}