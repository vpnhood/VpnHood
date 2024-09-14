using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Jobs;
using VpnHood.NetTester.CommandServers;
using VpnHood.NetTester.TcpTesters;

namespace VpnHood.NetTester;

internal class ClientApp : IDisposable
{
    private readonly IPEndPoint _serverEp;
    private readonly ServerConfig _serverConfig;
    private readonly ILogger _logger;

    private ClientApp(IPEndPoint serverEp, ServerConfig serverConfig, ILogger logger)
    {
        _serverEp = serverEp;
        _serverConfig = serverConfig;
        _logger = logger;
        JobRunner.Default.Interval = TimeSpan.FromMilliseconds(500);
    }

    public static async Task<ClientApp> Create(IPEndPoint serverEp, ServerConfig serverConfig, ILogger logger)
    {
        var clientApp = new ClientApp(serverEp: serverEp, serverConfig: serverConfig, logger: logger);
        await clientApp.ConfigureServer();
        return clientApp;
    }

    private async Task ConfigureServer()
    {
        // sent serverConfig to server via HttpClient
        var httpClient = new HttpClient();
        var content = new StringContent(JsonSerializer.Serialize(_serverConfig), Encoding.UTF8, "application/json");
        var response =  await httpClient.PostAsync($"http://{_serverEp}/config", content);
        response.EnsureSuccessStatusCode();
    }
    public async Task FullTcpTest(long uploadBytes, long downloadBytes, int connectionCount, CancellationToken cancellationToken)
    {
        await TcpTesterClient.StartFull(new IPEndPoint(_serverEp.Address, _serverConfig.TcpPort), 
            uploadBytes: uploadBytes, downloadBytes: downloadBytes, connectionCount: connectionCount,
            logger: _logger, cancellationToken: cancellationToken);
    }

    public void Dispose()
    {
        
    }
}