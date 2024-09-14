using System.Net;
using VpnHood.NetTester.CommandServers;
using VpnHood.NetTester.HttpTesters;
using VpnHood.NetTester.TcpTesters;

namespace VpnHood.NetTester;

internal class ServerApp(IPAddress listenerIp) : IDisposable
{
    private TcpTesterServer? _tcpTesterServer;
    private HttpTesterServer? _httpTesterServer;
    private CancellationTokenSource? _cancellationTokenSource;

    public void Configure(ServerConfig serverConfig)
    {
        // start server
        Stop();

        _cancellationTokenSource = new CancellationTokenSource();

        // start with new config
        if (serverConfig.TcpPort != null) {
            var tcpEndPoint = new IPEndPoint(listenerIp, serverConfig.TcpPort.Value);
            _tcpTesterServer = new TcpTesterServer();
            _tcpTesterServer?.Start(tcpEndPoint, _cancellationTokenSource.Token);
        }

        if (serverConfig.HttpPort != null) {
            var httpEndPoint = new IPEndPoint(listenerIp, serverConfig.HttpPort.Value);
            _httpTesterServer = new HttpTesterServer(httpEndPoint, _cancellationTokenSource.Token);
        }
    }

    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
        _tcpTesterServer?.Dispose();
        _httpTesterServer?.Dispose();
    }

    public void Dispose()
    {
        Stop();
    }

}