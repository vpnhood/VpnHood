using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;
using VpnHood.Core.Toolkit.Jobs;
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
    private Job? _autoUpdateJob;
    private ProxyAutoUpdateOptions _autoUpdateOptions;
    private bool _disposed;

    public bool IsEnabled { get; private set; }

    public ProgressStatus? Progress => _progressMonitor?.Progress;
    public ProxyEndPointManagerStatus Status => new() {
        ProxyEndPointInfos = _proxyEndPointEntries.Select(x => x.Info).ToArray(),
        IsLastConnectionSuccessful = _isLastConnectionSuccessful,
        AutoUpdate = _autoUpdateOptions.Interval > TimeSpan.Zero && _autoUpdateOptions.Url != null
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
        _autoUpdateOptions = proxyOptions.AutoUpdateOptions;
        IsEnabled = proxyOptions.ProxyEndPoints.Any();

        // load last NodeInfos
        var proxyInfos = JsonUtils.TryDeserializeFile<ProxyEndPointInfo[]>(_proxyEndPointInfosFile) ?? [];
        _proxyEndPointEntries = UpdateEntriesByOptions(proxyInfos.Select(x => new ProxyEndPointEntry(x)), proxyOptions)
            .ToArray();

        // Start auto-update if configured
        if (_autoUpdateOptions.Interval > TimeSpan.Zero && _autoUpdateOptions.Url != null)
            StartAutoUpdate(_autoUpdateOptions.Interval.Value);
    }

    public void UpdateOptions(ProxyOptions proxyOptions)
    {
        IsEnabled = proxyOptions.ProxyEndPoints.Any();
        _proxyEndPointEntries = UpdateEntriesByOptions(_proxyEndPointEntries, proxyOptions)
            .ToArray();

        // start new job if needed
        _autoUpdateOptions = proxyOptions.AutoUpdateOptions;
        if (_autoUpdateOptions.Interval > TimeSpan.Zero && _autoUpdateOptions.Url != null)
            StartAutoUpdate(_autoUpdateOptions.Interval.Value);
    }

    private void StartAutoUpdate(TimeSpan interval)
    {
        if (_autoUpdateJob?.Interval != interval)
            _autoUpdateJob?.Dispose();

        // Create and start the job
        _autoUpdateJob = new Job(
            UpdateFromUrlAsync,
            new JobOptions {
                Interval = interval,
                DueTime = TimeSpan.Zero, // Run immediately on start
                Name = "ProxyEndPointAutoUpdate",
                AutoStart = true
            });
    }

    private async ValueTask UpdateFromUrlAsync(CancellationToken cancellationToken)
    {
        if (_autoUpdateOptions.Url == null)
            return;

        try {
            VhLogger.Instance.LogInformation("Downloading proxy list from {Url}...", _autoUpdateOptions.Url);

            // memory more efficient to create a new client for infrequent requests
            using var httpClient = new HttpClient();
            var currentInfos = _proxyEndPointEntries.Select(x => x.Info).ToArray();
            var mergedEndPoints = await ProxyEndPointUpdater.UpdateFromUrlAsync(
                httpClient,
                _autoUpdateOptions.Url,
                currentInfos,
                _autoUpdateOptions.MaxItemCount,
                _autoUpdateOptions.MaxPenalty,
                cancellationToken);

            if (mergedEndPoints.Length == 0) {
                VhLogger.Instance.LogWarning("No proxies found in downloaded content from {Url}", _autoUpdateOptions.Url);
                return;
            }

            VhLogger.Instance.LogInformation("Downloaded and merged proxy list. Total proxies: {Count}", mergedEndPoints.Length);
            _proxyEndPointEntries = UpdateEntries(_proxyEndPointEntries, mergedEndPoints).ToArray();

            // Save updated list
            VhLogger.Instance.LogInformation("Updated proxy list. Total proxies: {Count}", _proxyEndPointEntries.Length);
            SaveNodeInfos();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Failed to update proxy list from {Url}", _autoUpdateOptions.Url);
        }
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
        ProxyEndPoint[] endPoints)
    {
        // remove old endpoints
        entries = entries
            .Where(x => endPoints.Any(y => y.Id == x.EndPoint.Id))
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
            var testEp = IPEndPoint.Parse("1.1.1.1:443");
            var tickCount = Environment.TickCount64;
            using var serverCheckCts = new CancellationTokenSource(_serverCheckTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, serverCheckCts.Token);
            await proxyClient.ConnectAsync(tcpClient, testEp, linkedCts.Token);
            return TimeSpan.FromMilliseconds(Environment.TickCount64 - tickCount);
        }
        finally {
            progressMonitor.IncrementCompleted();
        }
    }

    public async Task RemoveBadServers(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("Checking proxy servers for reachability...");
        var maxDegreeOfParallelism = Math.Max(50, _proxyEndPointEntries.Length);

        // initialize progress tracker
        _progressMonitor = new ProgressMonitor(
            totalTaskCount: _proxyEndPointEntries.Length,
            taskTimeout: _serverCheckTimeout,
            maxDegreeOfParallelism: maxDegreeOfParallelism);

        try {
            // concurrent results container
            var results = new ConcurrentBag<(
                ProxyEndPointEntry Entry, TcpClient? Client, 
                TimeSpan? Latency, Exception? Error)>();

            var parallelOptions = new ParallelOptions {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = maxDegreeOfParallelism
            };

            await Parallel.ForEachAsync(_proxyEndPointEntries, parallelOptions, async (entry, ct) => {
                TcpClient? tcpClient = null;
                try {
                    var proxyClient = await ProxyClientFactory.CreateProxyClient(entry.EndPoint);
                    tcpClient = _socketFactory.CreateTcpClient(proxyClient.ProxyEndPoint);
                    tcpClient.ReceiveBufferSize = 1024 * 4;
                    tcpClient.SendBufferSize = 1024 * 4;
                    var latency = await CheckConnectionAsync(proxyClient, tcpClient, _progressMonitor, ct);
                    results.Add((entry, tcpClient, latency, null));
                }
                catch (Exception ex) {
                    results.Add((entry, tcpClient, null, ex));
                }
            }).Vhc();

            // find the fastest server and set threshold
            var succeededTasks = results
                .Where(x => x.Error == null && x.Entry.EndPoint.IsEnabled && x.Latency.HasValue)
                .OrderBy(x => x.Latency!.Value)
                .ToArray();

            // set fastest server
            var fastestLatency = succeededTasks.Any() ? succeededTasks.First().Latency : null;

            // update server statuses and dispose clients
            foreach (var (entry, client, latency, error) in results) {
                client?.Dispose();

                if (error != null) {
                    entry.RecordFailed(error, _requestCount);
                    entry.EndPoint.IsEnabled = false;
                    continue;
                }

                entry.RecordSuccess(latency!.Value, fastestLatency, _requestCount);
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
        if (_disposed) return;
        _disposed = true;

        _autoUpdateJob?.Dispose();
        // Dispose HTTP client

        // save current NodeInfos
        VhUtils.TryInvoke("Save ProxyEndPoints status", SaveNodeInfos);
    }
}