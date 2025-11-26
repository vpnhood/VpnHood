using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Monitoring;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Proxies.EndPointManagement;

public class ProxyEndPointManager : IDisposable
{
    private ProxyEndPointEntry? _fastestEntry;
    private long _queuePosition;
    private readonly TimeSpan _serverCheckTimeout;
    private readonly ISocketFactory _socketFactory;
    private ProxyEndPointEntry[] _proxyEndPointEntries;
    private ProgressMonitor? _progressMonitor;
    private readonly string _proxyEndPointInfosFile;
    private Job? _autoUpdateJob;
    private ProxyAutoUpdateOptions _autoUpdateOptions;
    private bool _disposed;
    private bool _verifyTls;
    private readonly DateTime _sessionCreatedTime = FastDateTime.UtcNow;
    private readonly ProxyEndPointStatus _sessionStatus = new();
    private const int CheckServerMaxDegreeOfParallelism = 50;

    public bool IsEnabled { get; private set; }
    public bool UseRecentSucceeded { get; set; }
    public ProgressStatus? Progress => _progressMonitor?.Progress;


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
        _verifyTls = proxyOptions.VerifyTls;
        IsEnabled = proxyOptions.ProxyEndPoints.Any();

        // load last NodeInfos
        var data = JsonUtils.TryDeserializeFile<Data>(_proxyEndPointInfosFile) ?? new Data();
        _queuePosition = data.QueuePosition;
        _proxyEndPointEntries =
            UpdateEntriesByOptions(data.EndPointInfos.Select(x => new ProxyEndPointEntry(x)), proxyOptions)
                .ToArray();

        // Start auto-update if configured
        if (_autoUpdateOptions.Interval > TimeSpan.Zero && _autoUpdateOptions.Url != null)
            StartAutoUpdate(_autoUpdateOptions.Interval.Value);
    }

    public ProxyEndPointManagerStatus Status {
        get {
            lock (_sessionStatus)
                return new ProxyEndPointManagerStatus {
                    AutoUpdate = _autoUpdateOptions.Interval > TimeSpan.Zero && _autoUpdateOptions.Url != null,
                    SessionStatus = _sessionStatus,
                    ProxyEndPointInfos = _proxyEndPointEntries.Select(x => x.Info).ToArray(),
                    IsAnySucceeded = _proxyEndPointEntries.Any(x => x.Status.ErrorMessage is null),
                    SucceededServerCount =
                        _proxyEndPointEntries.Count(x => x.EndPoint.IsEnabled && x.Status.IsLastUsedSucceeded),
                    FailedServerCount = _proxyEndPointEntries.Count(x =>
                        x.EndPoint.IsEnabled && x.Status is { HasUsed: true, IsLastUsedSucceeded: false }),
                    UnknownServerCount =
                        _proxyEndPointEntries.Count(x => x.EndPoint.IsEnabled && x.Status is { HasUsed: false }),
                    DisabledServerCount = _proxyEndPointEntries.Count(x => !x.EndPoint.IsEnabled)
                };
        }
    }

    public void UpdateOptions(ProxyOptions proxyOptions)
    {
        IsEnabled = proxyOptions.ProxyEndPoints.Any();
        _verifyTls = proxyOptions.VerifyTls;
        _proxyEndPointEntries = UpdateEntriesByOptions(_proxyEndPointEntries, proxyOptions).ToArray();

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
            var newEndPoints = await ProxyEndPointUpdater
                .LoadFromUrlAsync(httpClient, _autoUpdateOptions.Url, cancellationToken).Vhc();
            var mergedEndPoints = ProxyEndPointUpdater.Merge(currentInfos, newEndPoints,
                _autoUpdateOptions.MaxItemCount, _autoUpdateOptions.MaxPenalty, _autoUpdateOptions.RemoveDuplicateIps);

            if (mergedEndPoints.Length == 0) {
                VhLogger.Instance.LogWarning("No proxies found in downloaded content from {Url}",
                    _autoUpdateOptions.Url);
                return;
            }

            VhLogger.Instance.LogInformation("Downloaded and merged proxy list. Total proxies: {Count}",
                mergedEndPoints.Length);
            _proxyEndPointEntries = UpdateEntries(_proxyEndPointEntries, mergedEndPoints,
                resetStates: false, keepEnabledState: true).ToArray();

            // Save updated list
            VhLogger.Instance.LogInformation("Updated proxy list. Total proxies: {Count}",
                _proxyEndPointEntries.Length);
            SaveNodeInfos();

            // Check servers
            await CheckServers(cancellationToken);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Failed to update proxy list from {Url}", _autoUpdateOptions.Url);
        }
    }

    private static IEnumerable<ProxyEndPointEntry> UpdateEntriesByOptions(IEnumerable<ProxyEndPointEntry> items,
        ProxyOptions options)
    {
        items = UpdateEntries(items, options.ProxyEndPoints,
            resetStates: options.ResetStates, keepEnabledState: false);
        return items;
    }

    // this is used to add new endpoints and remove old endpoints from options
    private static IEnumerable<ProxyEndPointEntry> UpdateEntries(
        IEnumerable<ProxyEndPointEntry> existingEntries,
        ProxyEndPoint[] newEntries,
        bool resetStates,
        bool keepEnabledState)
    {
        // create dictionary for existing entries using linq
        var existingEntryDic = existingEntries.DistinctBy(x => x.EndPoint.Id).ToDictionary(x => x.EndPoint.Id);
        var newEntryDic = newEntries.DistinctBy(x => x.Id).ToDictionary(x => x.Id);

        // update existing entries
        foreach (var existingEntry in existingEntryDic.Values) {
            var newEntry = newEntryDic.GetValueOrDefault(existingEntry.EndPoint.Id);
            var oldEndPoint = existingEntry.Info.EndPoint;

            // remove entries not in new list
            if (newEntry == null) {
                existingEntryDic.Remove(existingEntry.EndPoint.Id);
                continue;
            }

            // reset states of current items
            if (resetStates)
                existingEntry.Info.Status = new ProxyEndPointStatus();

            // update existing entries IsEnabled
            existingEntry.Info.EndPoint = newEntry;
            if (keepEnabledState)
                existingEntry.Info.EndPoint.IsEnabled = oldEndPoint.IsEnabled;
        }

        // add new endpoints
        foreach (var newEntry in newEntries.Where(x => !existingEntryDic.ContainsKey(x.Id))) {
            var entry = new ProxyEndPointEntry(new ProxyEndPointInfo {
                EndPoint = newEntry,
                Status = new ProxyEndPointStatus()
            });
            existingEntryDic.Add(newEntry.Id, entry);
        }

        return existingEntryDic.Select(x => x.Value);
    }

    private async Task<TimeSpan> CheckConnectionAsync(IProxyClient proxyClient,
        TcpClient tcpClient, ProgressMonitor? progressMonitor,
        CancellationToken cancellationToken)
    {
        using var serverCheckCts = new CancellationTokenSource(_serverCheckTimeout);
        try {
            var testEp = IPEndPoint.Parse("1.1.1.1:443");
            var tickCount = Environment.TickCount64;
            using var linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, serverCheckCts.Token);
            await proxyClient.ConnectAsync(tcpClient, testEp, linkedCts.Token).Vhc();
            var latency = TimeSpan.FromMilliseconds(Environment.TickCount64 - tickCount);

            // try to authenticate to make sure the proxy is fully functional
            if (_verifyTls) {
                var stream = new SslStream(tcpClient.GetStream());
                await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
                    TargetHost = "one.one.one.one"
                }, linkedCts.Token).Vhc();
            }

            return latency;
        }
        catch (OperationCanceledException ex) when (serverCheckCts.IsCancellationRequested) {
            throw new TimeoutException("Connection check timed out.", ex);
        }
        catch (AuthenticationException) {
            throw new ProxyClientException(SocketError.AccessDenied, "Verification of TLS connection failed.");
        }
        finally {
            progressMonitor?.IncrementCompleted();
        }
    }

    public Task CheckServers(CancellationToken cancellationToken)
    {
        const int satisfiedSuccessCount = 10;
        var endpoints = _proxyEndPointEntries
            .Where(x => x.EndPoint.IsEnabled)
            .OrderBy(x => x.Status.Quality)
            .ToArray();

        return CheckServers(endpoints, satisfiedSuccessCount, cancellationToken);
    }


    private async Task CheckServers(IEnumerable<ProxyEndPointEntry> endpoints,
        int satisfiedSuccessCount,
        CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("Checking proxy servers for reachability...");

        // initialize progress tracker
        _progressMonitor = new ProgressMonitor(
            totalTaskCount: _proxyEndPointEntries.Length,
            taskTimeout: _serverCheckTimeout,
            maxDegreeOfParallelism: CheckServerMaxDegreeOfParallelism);

        try {
            // concurrent results container
            var results = new ConcurrentBag<(
                ProxyEndPointEntry Entry, TcpClient? Client,
                TimeSpan? Latency, Exception? Error)>();

            var parallelOptions = new ParallelOptions {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = CheckServerMaxDegreeOfParallelism
            };

            // check all endpoints in parallel. If enough good servers found, stop checking more.
            var successCount = 0;
            await Parallel.ForEachAsync(endpoints, parallelOptions, async (entry, ct) => {
                TcpClient? tcpClient = null;
                try {
                    // do not stop 
                    if (successCount >= satisfiedSuccessCount &&
                        entry.Status.Quality is not StatusQuality.Unknown)
                        return;

                    var proxyClient = await ProxyClientFactory.CreateProxyClient(entry.EndPoint, ct).Vhc();
                    tcpClient = _socketFactory.CreateTcpClient(proxyClient.ProxyEndPoint);
                    tcpClient.ReceiveBufferSize = 1024 * 4;
                    tcpClient.SendBufferSize = 1024 * 4;
                    var latency = await CheckConnectionAsync(proxyClient, tcpClient, _progressMonitor, ct).Vhc();
                    results.Add((entry, tcpClient, latency, null));
                    Interlocked.Increment(ref successCount);
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

                // record success
                if (error is null) {
                    RecordSuccess(entry, latency: latency!.Value, fastestLatency: fastestLatency, checkMode: true);
                    continue;
                }

                // record failure
                if (!cancellationToken.IsCancellationRequested)
                    RecordFailed(entry, error, checkMode: true);

                // disable server if protocol is not supported
                var isProtocolRejected = error is ProxyClientException {
                    SocketErrorCode: SocketError.ProtocolNotSupported or SocketError.AccessDenied
                };
                if (isProtocolRejected)
                    entry.EndPoint.IsEnabled = false;
            }

            // make sure throw cancellation exception if cancelled
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally {
            _progressMonitor = null;
        }
    }


    // order by active state then Last Attempt
    private IEnumerable<ProxyEndPointEntry> GetOrderedEntriesQuery()
        => _proxyEndPointEntries
            .Where(x => x.EndPoint.IsEnabled)
            .Where(x => !UseRecentSucceeded || x.Status.LastSucceeded >= _sessionCreatedTime)
            .OrderBy(x => x.GetSortValue(_queuePosition))
            .ThenBy(x => x.Status.LastUsed);

    private readonly AsyncLock _connectLock = new();

    private async Task<ProxyEndPointEntry[]> GetOrderedEntries(CancellationToken cancellationToken)
    {
        // lock till get an ordered list with at least one succeeded server
        // if there is a failed server on top, all connection must wait till re-check is done
        using var scopeLock = await _connectLock.LockAsync(cancellationToken);
        Interlocked.Increment(ref _queuePosition);

        var endPointEntries = GetOrderedEntriesQuery().ToArray();
        if (endPointEntries.FirstOrDefault()?.Status.IsLastUsedSucceeded == true)
            return endPointEntries;

        // push the failed ones to the end and try again
        await CheckServers(endPointEntries, 1, cancellationToken);
        return GetOrderedEntriesQuery().ToArray();
    }


    public async Task<TcpClient> ConnectAsync(IPEndPoint ipEndPoint, CancellationToken cancellationToken)
    {
        // get ordered endpoints
        var entries = await GetOrderedEntries(cancellationToken);

        // try to connect to a proxy server
        foreach (var entry in entries) {
            var tickCount = Environment.TickCount64;
            TcpClient? tcpClient = null;
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);

            try {
                VhLogger.Instance.LogDebug(
                    "Connecting via a {ProxyType} proxy server {ProxyServer}...",
                    entry.EndPoint.Protocol, VhLogger.FormatHostName(entry.EndPoint.Host));

                // create proxy client
                var proxyClient = await ProxyClientFactory.CreateProxyClient(entry.EndPoint, cancellationToken).Vhc();
                tcpClient = _socketFactory.CreateTcpClient(proxyClient.ProxyEndPoint);

                // connect to the target endpoint
                await proxyClient.ConnectAsync(tcpClient, ipEndPoint, cancellationToken).Vhc();

                // find the fastest server
                var latency = TimeSpan.FromMilliseconds(Environment.TickCount64 - tickCount);
                if (!entries.Contains(_fastestEntry) ||
                    latency < _fastestEntry?.Status.Latency)
                    _fastestEntry = entry;

                RecordSuccess(entry, latency: latency, fastestLatency: _fastestEntry?.Status.Latency, checkMode: false);

                // disable other duplicate IPs if needed
                if (_autoUpdateOptions.RemoveDuplicateIps) {
                    var duplicateIps = entries.Where(x =>
                        x.EndPoint.Protocol == entry.EndPoint.Protocol &&
                        x.EndPoint.Host.Equals(entry.EndPoint.Host, StringComparison.OrdinalIgnoreCase) &&
                        x.EndPoint.Id != entry.EndPoint.Id);
                    foreach (var dup in duplicateIps) {
                        dup.EndPoint.IsEnabled = false;
                        var url = entry.EndPoint.BuildUrlWithoutPassword();
                        dup.Status.ErrorMessage = $"Duplicate IP disabled in favour of {url}";
                    }
                }

                return tcpClient;
            }
            catch (Exception ex) {
                var delay = TimeSpan.FromMilliseconds(Environment.TickCount64 - tickCount);
                VhLogger.Instance.LogError(ex,
                    "Failed to connect to {ProxyType} proxy server {ProxyServer}. Delay: {delay}",
                    entry.EndPoint.Protocol, VhLogger.FormatHostName(entry.EndPoint.Host), delay);

                // let's not assume bad server if caller cancelled the operation
                var isCallerCancelled = ex is OperationCanceledException && delay < _serverCheckTimeout;
                if (!isCallerCancelled)
                    RecordFailed(entry, ex, checkMode: false);

                tcpClient?.Dispose();
            }
        }

        // could not connect to any proxy server
        throw new SocketException((int)SocketError.NetworkUnreachable);
    }

    private void RecordSuccess(ProxyEndPointEntry entry, TimeSpan? latency, TimeSpan? fastestLatency,
        bool checkMode)
    {
        entry.RecordSuccess(latency!.Value, fastestLatency, _queuePosition);
        if (!checkMode)
            lock (_sessionStatus) {
                _sessionStatus.SucceededCount++;
                _sessionStatus.LastSucceeded = DateTime.UtcNow;
                _sessionStatus.QueuePosition = _queuePosition;
                _sessionStatus.Latency = entry.Status.Latency;
                _sessionStatus.Penalty = entry.Status.Penalty;
                _sessionStatus.ErrorMessage = null;
            }
    }

    private void RecordFailed(ProxyEndPointEntry entry, Exception error, bool checkMode)
    {
        entry.RecordFailed(error, _queuePosition);
        if (!checkMode)
            lock (_sessionStatus) {
                _sessionStatus.FailedCount++;
                _sessionStatus.LastFailed = DateTime.UtcNow;
                _sessionStatus.QueuePosition = _queuePosition;
                _sessionStatus.Latency = null;
                _sessionStatus.Penalty = entry.Status.Penalty;
                _sessionStatus.ErrorMessage = entry.Status.ErrorMessage;
            }
    }

    public void RecordFailed(TcpClient tcpClient, Exception ex)
    {
        // find the entry by matching proxy endpoint
        var remoteEndPoint = tcpClient.TryGetRemoteEndPoint();
        var entry = _proxyEndPointEntries.FirstOrDefault(x =>
            x.IpEndPoint?.Equals(remoteEndPoint) == true);

        if (entry != null) {
            entry.Status.SucceededCount--; // decrement succeeded count as the connection failed
            RecordFailed(entry, ex, checkMode: false);
        }
    }

    private void SaveNodeInfos()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_proxyEndPointInfosFile)!);
        var data = new Data {
            QueuePosition = _queuePosition,
            EndPointInfos = Status.ProxyEndPointInfos
        };
        File.WriteAllText(_proxyEndPointInfosFile, JsonSerializer.Serialize(data));
    }

    private class Data
    {
        public ProxyEndPointInfo[] EndPointInfos { get; init; } = [];
        public long QueuePosition { get; init; }
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