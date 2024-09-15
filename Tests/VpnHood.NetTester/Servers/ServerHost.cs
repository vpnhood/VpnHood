using System.Net;
using VpnHood.NetTester.Testers.HttpTesters;
using VpnHood.NetTester.Testers.TcpTesters;

namespace VpnHood.NetTester.Servers;

internal class ServerHost(IPAddress listenerIp) : IDisposable
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
        if (serverConfig.TcpPort != 0) {
            var tcpEndPoint = new IPEndPoint(listenerIp, serverConfig.TcpPort);
            _tcpTesterServer = new TcpTesterServer();
            _tcpTesterServer?.Start(tcpEndPoint, _cancellationTokenSource.Token);
        }

        if (serverConfig.HttpPort != 0) {
            var httpEndPoint = new IPEndPoint(listenerIp, serverConfig.HttpPort);
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