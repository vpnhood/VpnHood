using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Jobs;
using VpnHood.NetTester.HttpTesters;
using VpnHood.NetTester.TcpTesters;

namespace VpnHood.NetTester;

internal class ClientApp : IDisposable
{
    private readonly ClientOptions _clientOptions;
    private readonly ILogger _logger;
    private IPEndPoint ServerEndPoint => _clientOptions.ServerEndPoint;

    private ClientApp(ClientOptions clientOptions, ILogger logger)
    {
        _clientOptions = clientOptions;
        _logger = logger;
        JobRunner.Default.Interval = TimeSpan.FromMilliseconds(500);

        // dump clientOptions
        logger.LogInformation($"ClientOptions: {JsonSerializer.Serialize(clientOptions)}");
    }

    public static async Task<ClientApp> Create(ClientOptions clientOptions, ILogger logger)
    {
        var clientApp = new ClientApp(clientOptions: clientOptions, logger: logger);
        await clientApp.ConfigureServer();
        return clientApp;
    }

    public async Task StartTest(CancellationToken cancellationToken)
    {
        if (_clientOptions.TcpPort != 0) {
            await ConfigureServer();

            // test single
            await TcpTesterClient.StartSingle(new IPEndPoint(ServerEndPoint.Address, _clientOptions.TcpPort),
                upLength: _clientOptions.UpLength, downLength: _clientOptions.DownLength,
                logger: _logger, cancellationToken: cancellationToken);

            // test multi
            if (_clientOptions.ConnectionCount>0)
                await TcpTesterClient.StartMulti(new IPEndPoint(ServerEndPoint.Address, _clientOptions.TcpPort),
                    upLength: _clientOptions.UpLength, downLength: _clientOptions.DownLength, connectionCount: _clientOptions.ConnectionCount,
                    logger: _logger, cancellationToken: cancellationToken);
        }

        if (_clientOptions.HttpPort != 0) {
            await ConfigureServer();
            
            // test single
            await HttpTesterClient.StartSingle(new IPEndPoint(ServerEndPoint.Address, _clientOptions.TcpPort),
                upLength: _clientOptions.UpLength, downLength: _clientOptions.DownLength,
                logger: _logger, cancellationToken: cancellationToken);

            // test multi
            if (_clientOptions.ConnectionCount > 0)
                await HttpTesterClient.StartMulti(new IPEndPoint(ServerEndPoint.Address, _clientOptions.TcpPort),
                    upLength: _clientOptions.UpLength, downLength: _clientOptions.DownLength, connectionCount: _clientOptions.ConnectionCount,
                    logger: _logger, cancellationToken: cancellationToken);
        }
    }

    private async Task ConfigureServer()
    {
        // sent serverConfig to server via HttpClient
        var httpClient = new HttpClient();
        var content = new StringContent(JsonSerializer.Serialize(_clientOptions), Encoding.UTF8, "application/json");
        var response =  await httpClient.PostAsync($"http://{ServerEndPoint}/config", content);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        
    }
}