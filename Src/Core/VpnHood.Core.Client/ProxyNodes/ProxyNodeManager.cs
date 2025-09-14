using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Proxies;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Core.Client.ProxyNodes;

public class ProxyNodeManager
{
    private ProxyNodeItem? _fastestNode;
    private int _requestCount;
    private readonly TimeSpan _serverCheckTimeout;
    private readonly ISocketFactory _socketFactory;
    private ProxyNodeItem[] _proxyNodeItems;
    private ProgressTracker? _progressTracker;
    
    public ProxyNodeStatus[] ProxyNodeStatuses => _proxyNodeItems.Select(x => x.Status).ToArray();
    public bool IsEnabled => _proxyNodeItems.Any();
    public ProgressStatus? Progress => _progressTracker?.Progress;

    public ProxyNodeManager(
        ProxyNode[] proxyServerEndPoints,
        ISocketFactory socketFactory,
        TimeSpan? serverCheckTimeout = null)
    {
        _serverCheckTimeout = serverCheckTimeout ?? TimeSpan.FromSeconds(7);
        _proxyNodeItems = proxyServerEndPoints.Select(x => new ProxyNodeItem(x)).ToArray();
        _socketFactory = socketFactory;
    }

    public ProxyNode[] ProxyNodes {
        get {
            return _proxyNodeItems.Select(x => x.Node).ToArray();
        }
        set {
            // remove old servers and add new servers
            _proxyNodeItems = _proxyNodeItems
                .Where(x => value.Any(y => y.GetId() == x.Node.GetId()))
                .ToArray();
            
            // add new servers
            foreach (var newEndPoint in value) {
                if (_proxyNodeItems.All(x => x.Node.GetId() != newEndPoint.GetId())) {
                    var newServer = new ProxyNodeItem(newEndPoint);
                    _proxyNodeItems = _proxyNodeItems.Concat([newServer]).ToArray();
                }
            }
        }
    }

    private async Task<TimeSpan> CheckConnectionAsync(IProxyClient proxyClient, 
        TcpClient tcpClient, ProgressTracker progressTracker, 
        CancellationToken cancellationToken)
    {
        try {
            var tickCount = Environment.TickCount64;
            using var serverCheckCts = new CancellationTokenSource(_serverCheckTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, serverCheckCts.Token);
            await proxyClient.CheckConnectionAsync(tcpClient, linkedCts.Token).Vhc();
            return TimeSpan.FromMilliseconds(Environment.TickCount64 - tickCount);
        }
        finally{
            progressTracker.IncrementCompleted();
        }
    }

    public async Task RemoveBadServers(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Essential, "Checking proxy servers for reachability...");

        // initialize progress tracker
        _progressTracker = new ProgressTracker(
            totalTaskCount: _proxyNodeItems.Length,
            taskTimeout: _serverCheckTimeout,
            maxDegreeOfParallelism: _proxyNodeItems.Length);

        try {
            // connect to each proxy server and check if it is reachable
            List<(ProxyNodeItem, TcpClient, Task<TimeSpan>)> testTasks = [];
            foreach (var proxyServer in _proxyNodeItems) {
                var proxyClient = await ProxyClientFactory.CreateProxyClient(proxyServer.Node);
                var tcpClient = _socketFactory.CreateTcpClient(proxyClient.ProxyEndPoint);
                var checkTask = CheckConnectionAsync(proxyClient, tcpClient, _progressTracker, cancellationToken);
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
        finally {
            _progressTracker = null;
        }
    }

    public async Task<TcpClient> ConnectAsync(IPEndPoint ipEndPoint, CancellationToken cancellationToken)
    {
        _requestCount++;

        // order by active state then Last response duration
        var proxyServers = _proxyNodeItems
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
                    proxyServer.Node.Protocol, VhLogger.FormatHostName(proxyServer.Node.Host));

                var proxyClient = await ProxyClientFactory.CreateProxyClient(proxyServer.Node);
                tcpClient = _socketFactory.CreateTcpClient(proxyClient.ProxyEndPoint);
                await proxyClient.ConnectAsync(tcpClient, ipEndPoint, cancellationToken).Vhc();
                tcpClientOk = tcpClient;

                // find the fastest server
                var latency = TimeSpan.FromMilliseconds(Environment.TickCount64 - tickCount);
                if (!proxyServers.Contains(_fastestNode) ||
                    latency < _fastestNode?.Status.Latency)
                    _fastestNode = proxyServer;

                proxyServer.RecordSuccess(latency, fastestLatency: _fastestNode?.Status.Latency, _requestCount);
                break;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(GeneralEventId.Essential, ex,
                    "Failed to connect to {ProxyType} proxy server {ProxyServer}.",
                    proxyServer.Node.Protocol, VhLogger.FormatHostName(proxyServer.Node.Host));

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