using System.Net;
using EmbedIO;
using EmbedIO.WebApi;

namespace VpnHood.NetTester.CommandServers;

internal class CommandServer : IDisposable
{
    private readonly WebServer _webServer;
    public ServerApp ServerApp { get; set; }
    public IPEndPoint EndPoint { get; } 

    private CommandServer(IPEndPoint ipEndPoint)
    {
        ServerApp = new ServerApp(ipEndPoint.Address);
        EndPoint = ipEndPoint;

        // create web server
        var webServerOptions = new WebServerOptions();
        webServerOptions.AddUrlPrefix($"http://{ipEndPoint}");

        _webServer = new WebServer(webServerOptions)
            .WithWebApi("/", c => c.WithController(() => new CommandApiController(ServerApp)));

        _webServer.Start();
    }

    public static CommandServer Create(IPEndPoint ipEndPoint)
    {
        var ret = new CommandServer(ipEndPoint);
        return ret;
    }

    public void Dispose()
    {
        _webServer.Dispose();
    }
}