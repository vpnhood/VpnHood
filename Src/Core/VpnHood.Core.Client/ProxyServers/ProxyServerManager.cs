using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Proxies;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Core.Client.ProxyServers;

public class ProxyServerManager
{
    private readonly ISocketFactory _socketFactory;
    public ProxyServerStatus[] ProxyServerStatuses => _proxyServers.Select(x => x.Status).ToArray();
    private ProxyServer[] _proxyServers;
    public bool IsEnabled => _proxyServers.Any();

    public ProxyServerManager(ProxyServerEndPoint[] proxyServerEndPoints,
        ISocketFactory socketFactory)
    {
        _proxyServers = proxyServerEndPoints.Select(x => new ProxyServer(x)).ToArray();
        _socketFactory = socketFactory;
    }

    public ProxyServerEndPoint[] ProxyServerEndPoints {
        get {
            return _proxyServers.Select(x => x.EndPoint).ToArray();
        }
        set {
            // remove old servers and add new servers
            _proxyServers = _proxyServers
                .Where(x => value.Any(y => y.GetId() == x.EndPoint.GetId()))
                .ToArray();
            
            // add new servers
            foreach (var newEndPoint in value) {
                if (_proxyServers.All(x => x.EndPoint.GetId() != newEndPoint.GetId())) {
                    var newServer = new ProxyServer(newEndPoint);
                    _proxyServers = _proxyServers.Concat([newServer]).ToArray();
                }
            }
        }
    }

    private static async Task<TimeSpan> CheckConnectionAsync(IProxyClient proxyClient, TcpClient tcpClient,
        CancellationToken cancellationToken)
    {
        var tickCount = Environment.TickCount64;
        await proxyClient.CheckConnectionAsync(tcpClient, cancellationToken).Vhc();
        return TimeSpan.FromMilliseconds(Environment.TickCount64 - tickCount);
    }

    public async Task RemoveBadServers(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Essential, "Checking proxy servers for reachability...");

        // connect to each proxy server and check if it is reachable
        List<(ProxyServer, TcpClient, Task<TimeSpan>)> testTasks = [];
        foreach (var proxyServer in _proxyServers) {
            var proxyClient = await ProxyClientFactory.CreateProxyClient(proxyServer.EndPoint);
            var tcpClient = _socketFactory.CreateTcpClient(proxyClient.ProxyEndPoint);
            var checkTask = CheckConnectionAsync(proxyClient, tcpClient, cancellationToken);
            testTasks.Add((proxyServer, tcpClient, checkTask));
        }

        // wait for all tasks to complete even if some fail
        while (testTasks.Any(x => !x.Item3.IsCompleted))
            await Task.WhenAny(testTasks.Select(x => x.Item3)).Vhc();

        // find the fastest server and set threshold
        var succeededTasks = testTasks
            .Where(x => x.Item3.IsCompletedSuccessfully && x.Item1.Status.IsActive)
            .OrderBy(x => x.Item3.Result)
            .ToArray();

        // set fastest server
        TimeSpan? fastestLatency = succeededTasks.Any() ? succeededTasks.First().Item3.Result : null;

        // update server statuses
        foreach (var (proxyServer, tcpClient, task) in testTasks) {
            tcpClient.Dispose();

            // set failed
            if (!task.IsCompletedSuccessfully) {
                proxyServer.RecordFailed(task.Exception, _requestCount);
                proxyServer.Status.IsActive = false;
                continue;
            }

            // set active or poor active
            proxyServer.RecordSuccess(latency: task.Result, fastestLatency: fastestLatency, _requestCount);
        }
    }

    private ProxyServer? _fastestServer;
    private int _requestCount;
    public async Task<TcpClient> ConnectAsync(IPEndPoint ipEndPoint, CancellationToken cancellationToken)
    {
        _requestCount++;

        // order by active state then Last response duration
        var proxyServers = _proxyServers
            .Where(x => x.Status.IsActive)
            .OrderBy(x => x.GetSortValue(_requestCount))
            .ThenBy(x=>x.Status.LastUsedTime)
            .ToArray();

        // try to connect to a proxy server
        TcpClient? tcpClientOk = null;
        foreach (var proxyServer in proxyServers) {
            var tickCount = Environment.TickCount64;
            TcpClient? tcpClient = null;
            try {
                VhLogger.Instance.LogDebug(GeneralEventId.Essential,
                    "Connecting via a {ProxyType} proxy server {ProxyServer}...",
                    proxyServer.EndPoint.Type, VhLogger.FormatHostName(proxyServer.EndPoint.Host));

                var proxyClient = await ProxyClientFactory.CreateProxyClient(proxyServer.EndPoint);
                tcpClient = _socketFactory.CreateTcpClient(proxyClient.ProxyEndPoint);
                await proxyClient.ConnectAsync(tcpClient, ipEndPoint, cancellationToken).Vhc();
                tcpClientOk = tcpClient;

                // find the fastest server
                var latency = TimeSpan.FromMilliseconds(Environment.TickCount64 - tickCount);
                if (!proxyServers.Contains(_fastestServer) ||
                    latency < _fastestServer?.Status.Latency)
                    _fastestServer = proxyServer;

                proxyServer.RecordSuccess(latency, fastestLatency: _fastestServer?.Status.Latency, _requestCount);
                break;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(GeneralEventId.Essential, ex,
                    "Failed to connect to {ProxyType} proxy server {ProxyServer}.",
                    proxyServer.EndPoint.Type, VhLogger.FormatHostName(proxyServer.EndPoint.Host));

                proxyServer.RecordFailed(ex, _requestCount);
                tcpClient?.Dispose();
            }
        }

        // could not connect to any proxy server
        if (tcpClientOk is null)
            throw new SocketException((int)SocketError.NetworkUnreachable);

        return tcpClientOk;
    }
}