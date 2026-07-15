using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
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

public class ManagedProxyConnector : IProxyConnector
{
    private ProxyEndPointEntry? _fastestEntry;
    private long _queuePosition;
    private readonly TimeSpan _serverCheckTimeout;
    private readonly IProxyEndPointStore _store;
    // the most recent protected factory handed to us by a caller; the auto-update job has no caller of its
    // own, so it reuses this one and simply skips the check before the first connect
    private ISocketFactory? _lastSocketFactory;
    private ProxyEndPointEntry[] _proxyEndPointEntries;
    private ProgressMonitor? _progressMonitor;
    private Job? _autoUpdateJob;
    private readonly Job _flushJob;
    private ProxyAutoUpdateOptions _autoUpdateOptions;
    private bool _disposed;
    private bool _verifyTls;
    private readonly DateTime _sessionCreatedTime = FastDateTime.UtcNow;
    private readonly ProxySessionStatus _sessionStatus = new();
    private const int CheckServerMaxDegreeOfParallelism = 50;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(10);

    public bool IsEnabled { get; private set; }
    public bool UseRecentSucceeded { get; set; }
    public ProgressStatus? Progress => _progressMonitor?.Progress;

    private ManagedProxyConnector(
        ProxyOptions proxyOptions,
        IProxyEndPointStore store,
        TimeSpan? serverCheckTimeout)
    {
        _serverCheckTimeout = serverCheckTimeout ?? TimeSpan.FromSeconds(7);
        _store = store;
        _autoUpdateOptions = proxyOptions.AutoUpdateOptions;
        _verifyTls = proxyOptions.VerifyTls;
        _proxyEndPointEntries = [];

        // periodic write-behind of dirty statuses; cheap no-op when nothing changed
        _flushJob = new Job(FlushJob,
            new JobOptions {
                Interval = FlushInterval,
                Name = "ProxyEndPointFlush",
                AutoStart = true
            });
    }

    /// <summary>
    /// Builds a connector with its working set already loaded, so IsEnabled and Status are accurate as soon
    /// as it exists. Loading is async, hence a factory method rather than a constructor.
    /// </summary>
    public static async Task<ManagedProxyConnector> Create(
        ProxyOptions proxyOptions,
        IProxyEndPointStore store,
        TimeSpan? serverCheckTimeout = null,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var connector = new ManagedProxyConnector(proxyOptions, store, serverCheckTimeout);

        if (proxyOptions.ResetStates)
            await VhUtils.TryInvokeAsync("Reset proxy endpoint statuses", store.ResetStatuses).Vhc();

        // load the working set from the shared store; the store list is authoritative
        await connector.ReloadEntries().Vhc();

        // Start auto-update if configured
        if (connector._autoUpdateOptions.Interval > TimeSpan.Zero && connector._autoUpdateOptions.Url != null)
            connector.StartAutoUpdate(connector._autoUpdateOptions.Interval.Value);

        return connector;
    }

    public ProxyConnectorStatus Status {
        get {
            lock (_sessionStatus)
                return new ProxyConnectorStatus {
                    AutoUpdate = _autoUpdateOptions.Interval > TimeSpan.Zero && _autoUpdateOptions.Url != null,
                    SessionStatus = _sessionStatus,
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

    public IReadOnlyList<ProxyEndPointInfo> GetEndPointInfos() =>
        _proxyEndPointEntries.Select(x => x.Info).ToArray();

    private async Task<ProxyEndPointEntry[]> LoadEntries()
    {
        var records = await _store.List().Vhc();
        return records
            .Select(x => new ProxyEndPointEntry(x.ToInfo()))
            .ToArray();
    }

    private async Task ReloadEntries()
    {
        // dropping the old entries also drops their dirty flags
        _proxyEndPointEntries = await LoadEntries().Vhc();
        _queuePosition = await _store.GetQueuePosition().Vhc();
        IsEnabled = _proxyEndPointEntries.Length > 0;
    }

    public async Task UpdateOptions(ProxyOptions proxyOptions)
    {
        _verifyTls = proxyOptions.VerifyTls;

        if (proxyOptions.ResetStates) {
            // discard in-memory statuses instead of flushing them back over the app's reset
            await VhUtils.TryInvokeAsync("Reset proxy endpoint statuses", _store.ResetStatuses).Vhc();
        }
        else {
            await Flush().Vhc();
        }

        await ReloadEntries().Vhc();

        // start new job if needed
        _autoUpdateOptions = proxyOptions.AutoUpdateOptions;
        if (_autoUpdateOptions.Interval > TimeSpan.Zero && _autoUpdateOptions.Url != null)
            StartAutoUpdate(_autoUpdateOptions.Interval.Value);
    }

    private ValueTask FlushJob(CancellationToken cancellationToken)
    {
        return new ValueTask(Flush());
    }

    public async Task Flush()
    {
        var dirtyEntries = _proxyEndPointEntries.Where(x => x.IsDirty).ToArray();
        if (dirtyEntries.Length == 0)
            return;

        await _store.UpdateStatuses(dirtyEntries.Select(x => x.Info).ToArray()).Vhc();
        await _store.SetQueuePosition(_queuePosition).Vhc();
        foreach (var entry in dirtyEntries)
            entry.IsDirty = false;
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
            var newEndPoints = await ProxyEndPointUpdater
                .LoadFromUrlAsync(httpClient, _autoUpdateOptions.Url, cancellationToken).Vhc();

            if (newEndPoints.Length == 0) {
                VhLogger.Instance.LogWarning("No proxies found in downloaded content from {Url}",
                    _autoUpdateOptions.Url);
                return;
            }

            // merge into the shared store and reload the working set with preserved statuses
            await Flush().Vhc();
            await _store.Merge(newEndPoints, _autoUpdateOptions.MaxItemCount, _autoUpdateOptions.MaxPenalty,
                _autoUpdateOptions.RemoveDuplicateIps).Vhc();
            await ReloadEntries().Vhc();

            VhLogger.Instance.LogInformation("Downloaded and merged proxy list. Total proxies: {Count}",
                _proxyEndPointEntries.Length);

            // Check servers with the last factory a caller gave us. Before the first connect there is none;
            // GetOrderedEntries then checks the merged endpoints on that connect anyway.
            var socketFactory = _lastSocketFactory;
            if (socketFactory != null)
                await CheckServers(socketFactory, cancellationToken);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Failed to update proxy list from {Url}", _autoUpdateOptions.Url);
        }
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

    public Task CheckServers(ISocketFactory socketFactory, CancellationToken cancellationToken)
    {
        _lastSocketFactory = socketFactory;

        const int satisfiedSuccessCount = 10;
        var endpoints = _proxyEndPointEntries
            .Where(x => x.EndPoint.IsEnabled)
            .OrderBy(x => x.Status.Quality)
            .ToArray();

        return CheckServers(socketFactory, endpoints, satisfiedSuccessCount, cancellationToken);
    }


    private async Task CheckServers(ISocketFactory socketFactory,
        IEnumerable<ProxyEndPointEntry> endpoints,
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
                    tcpClient = socketFactory.CreateTcpClient(proxyClient.ProxyEndPoint);
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
                if (isProtocolRejected) {
                    entry.EndPoint.IsEnabled = false;
                    entry.IsDirty = true;
                }
            }

            // make sure throw cancellation exception if cancelled
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally {
            _progressMonitor = null;
            await Flush().Vhc();
        }
    }


    // order by active state then Last Attempt
    private ProxyEndPointEntry[] GetOrderedEntriesQuery(int maxPriorityFailed = 1)
    {
        var ordered =  _proxyEndPointEntries
            .Where(x => x.EndPoint.IsEnabled)
            .Where(x => !UseRecentSucceeded || x.Status.LastSucceeded >= _sessionCreatedTime)
            .OrderBy(x => x.GetSortValue(_queuePosition))
            .ThenBy(x => x.Status.LastUsed)
            .ToArray();

        // if there is at least one succeeded server, failed servers should not be more than 2
        // this is to avoid trying too many failed servers when there are good servers available

        // Only apply the rule when there is at least one succeeded server
        if (!ordered.Any(x => x.Status.IsLastUsedSucceeded))
            return ordered;

        // Keep the first maxPriorityFailed failed servers in-place (relative to succeeded ordering),
        // move the remaining failed servers to the end (preserving their order).
        var tailFailed = new List<ProxyEndPointEntry>();
        var result = new List<ProxyEndPointEntry>(ordered.Length);

        var failedCount = 0;

        foreach (var entry in ordered) {
            if (entry.Status.IsLastUsedSucceeded) {
                result.Add(entry);
                continue;
            }

            failedCount++;
            if (failedCount <= maxPriorityFailed)
                result.Add(entry);
            else
                tailFailed.Add(entry);
        }

        result.AddRange(tailFailed);
        return result.ToArray();
    }

    private readonly AsyncLock _connectLock = new();

    private async Task<ProxyEndPointEntry[]> GetOrderedEntries(ISocketFactory socketFactory,
        CancellationToken cancellationToken)
    {
        // lock till get an ordered list with at least one succeeded server
        // if there is a failed server on top, all connection must wait till re-check is done
        using var scopeLock = await _connectLock.LockAsync(cancellationToken);
        Interlocked.Increment(ref _queuePosition);

        var endPointEntries = GetOrderedEntriesQuery().ToArray();
        if (endPointEntries.FirstOrDefault()?.Status.IsLastUsedSucceeded == true)
            return endPointEntries;

        // push the failed ones to the end and try again
        await CheckServers(socketFactory, endPointEntries, 1, cancellationToken);
        return GetOrderedEntriesQuery().ToArray();
    }

    public async Task<TcpClient> ConnectAsync(ISocketFactory socketFactory, IPEndPoint ipEndPoint,
        Action? onAttempt, CancellationToken cancellationToken)
    {
        _lastSocketFactory = socketFactory;

        // get ordered endpoints
        var entries = await GetOrderedEntries(socketFactory, cancellationToken);

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
                tcpClient = socketFactory.CreateTcpClient(proxyClient.ProxyEndPoint);

                // connect to the target endpoint
                await proxyClient.ConnectAsync(tcpClient, ipEndPoint, cancellationToken).Vhc();

                // find the fastest server
                var latency = TimeSpan.FromMilliseconds(Environment.TickCount64 - tickCount);
                if (!entries.Contains(_fastestEntry) ||
                    latency < _fastestEntry?.Status.Latency)
                    _fastestEntry = entry;

                RecordSuccess(entry, latency: latency, fastestLatency: _fastestEntry?.Status.Latency, checkMode: false);
                onAttempt?.Invoke();

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
                        dup.IsDirty = true;
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
                if (!isCallerCancelled) {
                    RecordFailed(entry, ex, checkMode: false);
                    onAttempt?.Invoke();
                }

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
        if (!checkMode) {
            lock (_sessionStatus) {
                _sessionStatus.SucceededCount++;
                _sessionStatus.LastSucceeded = DateTime.UtcNow;
                _sessionStatus.Latency = entry.Status.Latency;
                _sessionStatus.ErrorMessage = null;
            }
        }
    }

    private void RecordFailed(ProxyEndPointEntry entry, Exception error, bool checkMode)
    {
        entry.RecordFailed(error, _queuePosition);
        if (!checkMode) {
            lock (_sessionStatus) {
                _sessionStatus.FailedCount++;
                _sessionStatus.LastFailed = DateTime.UtcNow;
                _sessionStatus.Latency = null;
                _sessionStatus.ErrorMessage = entry.Status.ErrorMessage;
            }
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _autoUpdateJob?.Dispose();
        _flushJob.Dispose();

        // persist pending statuses to the shared store (Flush already swallows its own errors)
        await Flush().Vhc();
        _store.Dispose();
    }
}
