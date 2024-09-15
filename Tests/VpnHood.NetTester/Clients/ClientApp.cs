using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Jobs;
using VpnHood.Common.Logging;
using VpnHood.NetTester.Servers;
using VpnHood.NetTester.Testers.HttpTesters;
using VpnHood.NetTester.Testers.TcpTesters;

namespace VpnHood.NetTester.Clients;

internal class ClientApp : IDisposable
{
    private readonly ClientOptions _clientOptions;
    private IPEndPoint ServerEndPoint => _clientOptions.ServerEndPoint;

    private ClientApp(ClientOptions clientOptions)
    {
        _clientOptions = clientOptions;
        JobRunner.Default.Interval = TimeSpan.FromMilliseconds(500);

        // dump clientOptions
        VhLogger.Instance.LogInformation($"ClientOptions: {JsonSerializer.Serialize(clientOptions)}");
    }

    public static async Task<ClientApp> Create(ClientOptions clientOptions)
    {
        var clientApp = new ClientApp(clientOptions: clientOptions);
        await clientApp.ConfigureServer();
        return clientApp;
    }

    public async Task StartTest(CancellationToken cancellationToken)
    {
        if (_clientOptions.TcpPort != 0) {
            await ConfigureServer();

            // test single
            if (_clientOptions.Single)
                await TcpTesterClient.StartSingle(new IPEndPoint(ServerEndPoint.Address, _clientOptions.TcpPort),
                    upLength: _clientOptions.UpLength, downLength: _clientOptions.DownLength,
                    cancellationToken: cancellationToken);

            // test multi
            if (_clientOptions.Multi > 0)
                await TcpTesterClient.StartMulti(new IPEndPoint(ServerEndPoint.Address, _clientOptions.TcpPort),
                    upLength: _clientOptions.UpLength, downLength: _clientOptions.DownLength,
                    multi: _clientOptions.Multi,
                    cancellationToken: cancellationToken);
        }

        if (_clientOptions.HttpPort != 0) {
            await ConfigureServer();

            // test single
            if (_clientOptions.Single)
                await HttpTesterClient.StartSingle(new IPEndPoint(ServerEndPoint.Address, _clientOptions.HttpPort),
                    upLength: _clientOptions.UpLength, downLength: _clientOptions.DownLength,
                    cancellationToken: cancellationToken);

            // test multi
            if (_clientOptions.Multi > 0)
                await HttpTesterClient.StartMulti(new IPEndPoint(ServerEndPoint.Address, _clientOptions.HttpPort),
                    upLength: _clientOptions.UpLength, downLength: _clientOptions.DownLength,
                    connectionCount: _clientOptions.Multi,
                    cancellationToken: cancellationToken);
        }
    }

    private async Task ConfigureServer()
    {
        var serverConfig = new ServerConfig {
            TcpPort = _clientOptions.TcpPort,
            HttpPort = _clientOptions.HttpPort,
            HttpsPort = _clientOptions.HttpsPort,
            HttpsDomain = _clientOptions.HttpsDomain,
        };

        // sent serverConfig to server via HttpClient
        var httpClient = new HttpClient();
        var content = new StringContent(JsonSerializer.Serialize(serverConfig), Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync($"http://{ServerEndPoint}/config", content);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
    }
}