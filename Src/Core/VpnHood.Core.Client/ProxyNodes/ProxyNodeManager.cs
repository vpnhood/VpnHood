using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.ProxyNodes;
using VpnHood.Core.Proxies;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Core.Client.ProxyNodes;

public class ProxyNodeManager : IDisposable
{
    private ProxyNodeItem? _fastestNode;
    private int _requestCount;
    private readonly TimeSpan _serverCheckTimeout;
    private readonly ISocketFactory _socketFactory;
    private ProxyNodeItem[] _proxyNodeItems;
    private ProgressTracker? _progressTracker;
    private readonly string _proxyNodeInfosFile;
    private bool _isLastConnectionSuccessful;

    public bool IsEnabled { get; private set; }
    public ProgressStatus? Progress => _progressTracker?.Progress;
    public ProxyManagerStatus Status => new() {
        ProxyNodeInfos = _proxyNodeItems.Select(x => x.Info).ToArray(),
        IsLastConnectionSuccessful = _isLastConnectionSuccessful
    };

    public ProxyNodeManager(
        ProxyOptions proxyOptions,
        string storagePath,
        ISocketFactory socketFactory,
        TimeSpan? serverCheckTimeout = null)
    {
        _serverCheckTimeout = serverCheckTimeout ?? TimeSpan.FromSeconds(7);
        _socketFactory = socketFactory;
        _proxyNodeInfosFile = Path.Combine(storagePath, "proxies.json");
        IsEnabled = proxyOptions.ProxyNodes.Any();

        // load last NodeInfos
        var proxyInfos = JsonUtils.TryDeserializeFile<ProxyNodeInfo[]>(_proxyNodeInfosFile) ?? [];
        _proxyNodeItems = UpdateItemsByOptions(proxyInfos.Select(x => new ProxyNodeItem(x)), proxyOptions)
            .ToArray();
    }

    public void UpdateOptions(ProxyOptions proxyOptions)
    {
        IsEnabled = proxyOptions.ProxyNodes.Any();
        _proxyNodeItems = UpdateItemsByOptions(_proxyNodeItems, proxyOptions)
            .ToArray();
    }

    private static IEnumerable<ProxyNodeItem> UpdateItemsByOptions(IEnumerable<ProxyNodeItem> items, ProxyOptions options)
    {
        items = UpdateProxyItems(items, options.ProxyNodes).ToArray();
        if (options.ResetStates)
            foreach (var item in items)
                item.Info.Status = new ProxyNodeStatus();

        return items;
    }

    private static IEnumerable<ProxyNodeItem> UpdateProxyItems(
        IEnumerable<ProxyNodeItem> proxyItems, ProxyNode[] proxyNodes)
    {
        // remove old nodes
        proxyItems = proxyItems
            .Where(x => proxyNodes.Any(y => y.Id == x.Node.Id))
            .ToArray();

        // add new nodes
        foreach (var proxyNode in proxyNodes) {
            if (proxyItems.All(x => x.Node.Id != proxyNode.Id)) {
                var newNode = new ProxyNodeItem(new ProxyNodeInfo {
                    Node = proxyNode
                });
                proxyItems = proxyItems.Concat([newNode]).ToArray();
            }
        }

        return proxyItems;
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
        finally {
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
            foreach (var proxyNodeItem in _proxyNodeItems) {
                var proxyClient = await ProxyClientFactory.CreateProxyClient(proxyNodeItem.Node);
                var tcpClient = _socketFactory.CreateTcpClient(proxyClient.ProxyEndPoint);
                var checkTask = CheckConnectionAsync(proxyClient, tcpClient, _progressTracker, cancellationToken);
                testTasks.Add((proxyNodeItem, tcpClient, checkTask));
            }

            // wait for all tasks to complete even if some fail
            while (testTasks.Any(x => !x.Item3.IsCompleted))
                await Task.WhenAny(testTasks.Select(x => x.Item3)).Vhc();

            // find the fastest server and set threshold
            var succeededTasks = testTasks
                .Where(x => x.Item3.IsCompletedSuccessfully && x.Item1.Node.IsEnabled)
                .OrderBy(x => x.Item3.Result)
                .ToArray();

            // set fastest server
            TimeSpan? fastestLatency = succeededTasks.Any() ? succeededTasks.First().Item3.Result : null;

            // update server statuses
            foreach (var (proxyNodeItem, tcpClient, task) in testTasks) {
                tcpClient.Dispose();

                // set failed
                if (!task.IsCompletedSuccessfully) {
                    proxyNodeItem.RecordFailed(task.Exception, _requestCount);
                    proxyNodeItem.Node.IsEnabled = false;
                    continue;
                }

                // set active or poor active
                proxyNodeItem.RecordSuccess(latency: task.Result, fastestLatency: fastestLatency, _requestCount);
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
            .Where(x => x.Node.IsEnabled)
            .OrderBy(x => x.GetSortValue(_requestCount))
            .ThenBy(x => x.Status.LastUsedTime)
            .ToArray();

        // try to connect to a proxy server
        TcpClient? tcpClientOk = null;
        foreach (var proxyNodeItem in proxyServers) {
            var tickCount = Environment.TickCount64;
            TcpClient? tcpClient = null;
            try {
                VhLogger.Instance.LogDebug(GeneralEventId.Essential,
                    "Connecting via a {ProxyType} proxy server {ProxyServer}...",
                    proxyNodeItem.Node.Protocol, VhLogger.FormatHostName(proxyNodeItem.Node.Host));

                var proxyClient = await ProxyClientFactory.CreateProxyClient(proxyNodeItem.Node);
                tcpClient = _socketFactory.CreateTcpClient(proxyClient.ProxyEndPoint);
                await proxyClient.ConnectAsync(tcpClient, ipEndPoint, cancellationToken).Vhc();
                tcpClientOk = tcpClient;

                // find the fastest server
                var latency = TimeSpan.FromMilliseconds(Environment.TickCount64 - tickCount);
                if (!proxyServers.Contains(_fastestNode) ||
                    latency < _fastestNode?.Status.Latency)
                    _fastestNode = proxyNodeItem;

                proxyNodeItem.RecordSuccess(latency, fastestLatency: _fastestNode?.Status.Latency, _requestCount);
                _isLastConnectionSuccessful = true;
                break;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(GeneralEventId.Essential, ex,
                    "Failed to connect to {ProxyType} proxy server {ProxyServer}.",
                    proxyNodeItem.Node.Protocol, VhLogger.FormatHostName(proxyNodeItem.Node.Host));

                _isLastConnectionSuccessful = false;
                proxyNodeItem.RecordFailed(ex, _requestCount);
                tcpClient?.Dispose();
            }
        }

        // could not connect to any proxy server
        if (tcpClientOk is null)
            throw new SocketException((int)SocketError.NetworkUnreachable);

        return tcpClientOk;
    }

    private void SaveNodeInfos()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_proxyNodeInfosFile)!);
        File.WriteAllText(_proxyNodeInfosFile, JsonSerializer.Serialize(Status.ProxyNodeInfos));
    }

    public void Dispose()
    {
        // save current NodeInfos
        VhUtils.TryInvoke("Save ProxyNodes status", SaveNodeInfos);
    }
}