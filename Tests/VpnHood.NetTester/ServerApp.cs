using System.Net;
using VpnHood.NetTester.CommandServers;
using VpnHood.NetTester.TcpTesters;

namespace VpnHood.NetTester;

internal class ServerApp(IPAddress listenerIp) : IDisposable
{
    private TcpTesterServer? _tcpTesterServer;
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
    }

    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
        _tcpTesterServer?.Dispose();
    }

    public void Dispose()
    {
        Stop();
    }

}