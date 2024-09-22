using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Jobs;
using VpnHood.Common.Logging;
using VpnHood.NetTester.Servers;
using VpnHood.NetTester.Testers;
using VpnHood.NetTester.Testers.HttpTesters;
using VpnHood.NetTester.Testers.QuicTesters;
using VpnHood.NetTester.Testers.TcpTesters;
using VpnHood.NetTester.Utils;

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
        VhLogger.Instance.LogInformation("ClientOptions: {ClientOptions}",
            JsonSerializer.Serialize(clientOptions, new JsonSerializerOptions {
                WriteIndented = true
            }));

        if (clientOptions.IsDebug)
            StreamRandomReader.ReadDelay = TimeSpan.FromMicroseconds(100); //todo
    }

    public static async Task<ClientApp> Create(ClientOptions clientOptions)
    {
        var clientApp = new ClientApp(clientOptions: clientOptions);
        await clientApp.ConfigureServer();
        return clientApp;
    }

    public async Task StartTest(CancellationToken cancellationToken)
    {
        var streamTesterClient = new List<IStreamTesterClient>();

        // add tester clients
        if (_clientOptions.TcpPort != 0)
            streamTesterClient.Add(new TcpTesterClient(
                new IPEndPoint(ServerEndPoint.Address, _clientOptions.TcpPort)));

        if (_clientOptions.HttpPort != 0)
            streamTesterClient.Add(new HttpTesterClient(
                new IPEndPoint(ServerEndPoint.Address, _clientOptions.HttpPort),
                domain: _clientOptions.Domain,
                isHttps: false,
                timeout: TimeSpan.FromSeconds(_clientOptions.Timeout)));

        if (_clientOptions.HttpsPort != 0)
            streamTesterClient.Add(new HttpTesterClient(
                new IPEndPoint(ServerEndPoint.Address, _clientOptions.HttpsPort),
                domain: _clientOptions.Domain,
                isHttps: true,
                timeout: TimeSpan.FromSeconds(_clientOptions.Timeout)));

        if (_clientOptions.QuicPort != 0)
            streamTesterClient.Add(new QuicTesterClient(
                new IPEndPoint(ServerEndPoint.Address, _clientOptions.HttpsPort),
                domain: _clientOptions.Domain,
                timeout: TimeSpan.FromSeconds(_clientOptions.Timeout)));

        if (_clientOptions.Url != null)
            streamTesterClient.Add(new UrlTesterClient(
                _clientOptions.Url,
                _clientOptions.UrlIp,
                timeout: TimeSpan.FromSeconds(_clientOptions.Timeout)));


        // start test
        foreach (var testerClient in streamTesterClient) {
            // test single
            if (_clientOptions.Single)
                await testerClient.Start(
                    upSize: _clientOptions.UpSize * 1000000,
                    downSize: _clientOptions.DownSize * 1000000,
                    connectionCount: 1,
                    cancellationToken: cancellationToken);

            // test multi
            if (_clientOptions.Multi > 0)
                await testerClient.Start(
                    upSize: _clientOptions.UpSize * 1000000, 
                    downSize: _clientOptions.DownSize * 1000000,
                    connectionCount: _clientOptions.Multi,
                    cancellationToken: cancellationToken);

        }
    }

    private async Task ConfigureServer()
    {
        VhLogger.Instance.LogInformation("Configuring server...");

        var serverConfig = new ServerConfig {
            TcpPort = _clientOptions.TcpPort,
            HttpPort = _clientOptions.HttpPort,
            HttpsPort = _clientOptions.HttpsPort,
            QuicPort = _clientOptions.QuicPort,
            Domain = _clientOptions.Domain,
            IsValidDomain = _clientOptions.IsValidDomain
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