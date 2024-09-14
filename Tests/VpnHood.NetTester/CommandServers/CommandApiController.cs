using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi;

namespace VpnHood.NetTester.CommandServers;

internal class CommandApiController(ServerApp serverApp) : WebApiController
{
    // ReSharper disable once UnusedMember.Local
    [Route(HttpVerbs.Post, "/config")]
    public async Task Configure()
    {
        var serverConfig = await HttpContext.GetRequestDataAsync<ServerConfig>();
        serverApp.Configure(serverConfig);
    }
}