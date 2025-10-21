using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Monitoring;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Proxies.EndPointManagement;

public class ProxyEndPointManager : IDisposable
{
    private ProxyEndPointEntry? _fastestEntry;
    private int _requestCount;
    private readonly TimeSpan _serverCheckTimeout;
    private readonly ISocketFactory _socketFactory;
    private ProxyEndPointEntry[] _proxyEndPointEntries;
    private ProgressMonitor? _progressMonitor;
    private readonly string _proxyEndPointInfosFile;
    private bool _isLastConnectionSuccessful;

    public bool IsEnabled { get; private set; }
    public ProgressStatus? Progress => _progressMonitor?.Progress;
    public ProxyEndPointManagerStatus Status => new() {
        ProxyEndPointInfos = _proxyEndPointEntries.Select(x => x.Info).ToArray(),
        IsLastConnectionSuccessful = _isLastConnectionSuccessful
    };

    public ProxyEndPointManager(
        ProxyOptions proxyOptions,
        string storagePath,
        ISocketFactory socketFactory,
        TimeSpan? serverCheckTimeout = null)
    {
        _serverCheckTimeout = serverCheckTimeout ?? TimeSpan.FromSeconds(7);
        _socketFactory = socketFactory;
        _proxyEndPointInfosFile = Path.Combine(storagePath, "proxies.json");
        IsEnabled = proxyOptions.ProxyEndPoints.Any();

        // load last NodeInfos
        var proxyInfos = JsonUtils.TryDeserializeFile<ProxyEndPointInfo[]>(_proxyEndPointInfosFile) ?? [];
        _proxyEndPointEntries = UpdateEntriesByOptions(proxyInfos.Select(x => new ProxyEndPointEntry(x)), proxyOptions)
            .ToArray();
    }

    public void UpdateOptions(ProxyOptions proxyOptions)
    {
        IsEnabled = proxyOptions.ProxyEndPoints.Any();
        _proxyEndPointEntries = UpdateEntriesByOptions(_proxyEndPointEntries, proxyOptions)
            .ToArray();
    }

    private static IEnumerable<ProxyEndPointEntry> UpdateEntriesByOptions(IEnumerable<ProxyEndPointEntry> items, ProxyOptions options)
    {
        items = UpdateEntries(items, options.ProxyEndPoints).ToArray();
        if (options.ResetStates)
            foreach (var item in items)
                item.Info.Status = new ProxyEndPointStatus();

        return items;
    }

    // this is used to add new endpoints and remove old endpoints from options
    private static IEnumerable<ProxyEndPointEntry> UpdateEntries(
        IEnumerable<ProxyEndPointEntry> entries, 
        ProxyEndPoint[] endPoints,
        int minPenalty = -1)
    {
        // remove old endpoints
        entries = entries
            .Where(x => endPoints.Any(y =>
                y.Id == x.EndPoint.Id || 
                x.Status.Penalty > minPenalty))
            .ToArray();

        // add new endpoints
        foreach (var proxyEndPoint in endPoints) {
            if (entries.All(x => x.EndPoint.Id != proxyEndPoint.Id)) {
                var newNode = new ProxyEndPointEntry(new ProxyEndPointInfo {
                    EndPoint = proxyEndPoint
                });
                entries = entries.Concat([newNode]).ToArray();
            }
        }

        return entries;
    }

    private async Task<TimeSpan> CheckConnectionAsync(IProxyClient proxyClient,
        TcpClient tcpClient, ProgressMonitor progressMonitor,
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
            progressMonitor.IncrementCompleted();
        }
    }

    public async Task RemoveBadServers(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("Checking proxy servers for reachability...");

        // initialize progress tracker
        _progressMonitor = new ProgressMonitor(
            totalTaskCount: _proxyEndPointEntries.Length,
            taskTimeout: _serverCheckTimeout,
            maxDegreeOfParallelism: _proxyEndPointEntries.Length);

        try {
            // connect to each proxy server and check if it is reachable
            List<(ProxyEndPointEntry, TcpClient, Task<TimeSpan>)> testTasks = [];
            foreach (var proxyEndPointItem in _proxyEndPointEntries) {
                var proxyClient = await ProxyClientFactory.CreateProxyClient(proxyEndPointItem.EndPoint);
                var tcpClient = _socketFactory.CreateTcpClient(proxyClient.ProxyEndPoint);
                var checkTask = CheckConnectionAsync(proxyClient, tcpClient, _progressMonitor, cancellationToken);
                testTasks.Add((proxyEndPointItem, tcpClient, checkTask));
            }

            // wait for all tasks to complete even if some fail
            while (testTasks.Any(x => !x.Item3.IsCompleted))
                await Task.WhenAny(testTasks.Select(x => x.Item3)).Vhc();

            // find the fastest server and set threshold
            var succeededTasks = testTasks
                .Where(x => x.Item3.IsCompletedSuccessfully && x.Item1.EndPoint.IsEnabled)
                .OrderBy(x => x.Item3.Result)
                .ToArray();

            // set fastest server
            TimeSpan? fastestLatency = succeededTasks.Any() ? succeededTasks.First().Item3.Result : null;

            // update server statuses
            foreach (var (proxyEndPointItem, tcpClient, task) in testTasks) {
                tcpClient.Dispose();

                // set failed
                if (!task.IsCompletedSuccessfully) {
                    proxyEndPointItem.RecordFailed(task.Exception, _requestCount);
                    proxyEndPointItem.EndPoint.IsEnabled = false;
                    continue;
                }

                // set active or poor active
                proxyEndPointItem.RecordSuccess(latency: task.Result, fastestLatency: fastestLatency, _requestCount);
            }
        }
        finally {
            _progressMonitor = null;
        }
    }

    public async Task<TcpClient> ConnectAsync(IPEndPoint ipEndPoint, CancellationToken cancellationToken)
    {
        _requestCount++;

        // order by active state then Last response duration
        var proxyServers = _proxyEndPointEntries
            .Where(x => x.EndPoint.IsEnabled)
            .OrderBy(x => x.GetSortValue(_requestCount))
            .ThenBy(x => x.Status.LastUsedTime)
            .ToArray();

        // try to connect to a proxy server
        TcpClient? tcpClientOk = null;
        foreach (var proxyEndPointItem in proxyServers) {
            var tickCount = Environment.TickCount64;
            TcpClient? tcpClient = null;
            try {
                VhLogger.Instance.LogDebug(
                    "Connecting via a {ProxyType} proxy server {ProxyServer}...",
                    proxyEndPointItem.EndPoint.Protocol, VhLogger.FormatHostName(proxyEndPointItem.EndPoint.Host));

                var proxyClient = await ProxyClientFactory.CreateProxyClient(proxyEndPointItem.EndPoint);
                tcpClient = _socketFactory.CreateTcpClient(proxyClient.ProxyEndPoint);
                await proxyClient.ConnectAsync(tcpClient, ipEndPoint, cancellationToken).Vhc();
                tcpClientOk = tcpClient;

                // find the fastest server
                var latency = TimeSpan.FromMilliseconds(Environment.TickCount64 - tickCount);
                if (!proxyServers.Contains(_fastestEntry) ||
                    latency < _fastestEntry?.Status.Latency)
                    _fastestEntry = proxyEndPointItem;

                proxyEndPointItem.RecordSuccess(latency, fastestLatency: _fastestEntry?.Status.Latency, _requestCount);
                _isLastConnectionSuccessful = true;
                break;
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex,
                    "Failed to connect to {ProxyType} proxy server {ProxyServer}.",
                    proxyEndPointItem.EndPoint.Protocol, VhLogger.FormatHostName(proxyEndPointItem.EndPoint.Host));

                _isLastConnectionSuccessful = false;
                proxyEndPointItem.RecordFailed(ex, _requestCount);
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
        Directory.CreateDirectory(Path.GetDirectoryName(_proxyEndPointInfosFile)!);
        File.WriteAllText(_proxyEndPointInfosFile, JsonSerializer.Serialize(Status.ProxyEndPointInfos));
    }

    public void Dispose()
    {
        // save current NodeInfos
        VhUtils.TryInvoke("Save ProxyEndPoints status", SaveNodeInfos);
    }
}