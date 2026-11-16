using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Net.Proxies;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Jobs;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Monitoring;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Proxies.Management;

/// <summary>
/// Provides the VPN's connections through a managed list of proxies, kept in an <see cref="IProxyEndPointStore"/>: it
/// rates the endpoints by their successes and failures, rotates them, checks them, and may update the list from a
/// URL.
/// </summary>
/// <remarks>
/// <para>
/// A connection tries the enabled endpoints in turn, the one whose turn comes soonest first: an endpoint's next turn
/// moves back by 3 for each point of its penalty. While any endpoint succeeded last, only the first of those that
/// did not keeps its place, and the rest go to the end. When the first endpoint in that order did not succeed last,
/// the endpoints are checked first: the unrated ones all, the others until one succeeds.
/// </para>
/// <para>
/// A check connects through each enabled endpoint, up to 50 at a time, to <c>1.1.1.1:443</c>, with
/// <see cref="ProxyOptions.VerifyTls"/> also through a TLS handshake with <c>one.one.one.one</c>, and records each
/// outcome; once 10 have succeeded, it checks only those never rated. An endpoint whose check fails with a
/// <see cref="VpnHood.Net.Proxies.ProxyClientException"/> of <see cref="System.Net.Sockets.SocketError.ProtocolNotSupported"/>
/// or <see cref="System.Net.Sockets.SocketError.AccessDenied"/>, as an <see cref="System.Security.Authentication.AuthenticationException"/>
/// in the check's TLS handshake becomes, is disabled. The statuses are saved to the store every 10 seconds, after a check, and at the disposal.
/// </para>
/// </remarks>
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
    private readonly AsyncLock _disposeLock = new();
    private bool _verifyTls;
    private readonly DateTime _sessionCreatedTime = FastDateTime.UtcNow;
    private readonly ProxySessionStatus _sessionStatus = new();
    // the proxy that made each connection handed out, for a failure after its connect, as on TLS; weak, as most
    // connections never fail
    private readonly ConditionalWeakTable<TcpClient, HandedOutConnection> _handedOutConnections = new();
    private const int CheckServerMaxDegreeOfParallelism = 50;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(10);

    /// <inheritdoc/>
    /// <value><see langword="true"/> while the store has any endpoint, as last loaded.</value>
    public bool IsEnabled { get; private set; }

    /// <inheritdoc/>
    public bool UseRecentSucceeded { get; set; }

    /// <inheritdoc/>
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
    /// Creates a connector with its endpoints already loaded from the store, so <see cref="IsEnabled"/> and
    /// <see cref="Status"/> hold from the start, and starts its update from a URL where the options ask for one.
    /// </summary>
    /// <param name="proxyOptions">
    /// The options; with <see cref="ProxyOptions.ResetStates"/>, the stored statuses are cleared first, which logs a
    /// failure rather than throwing it.
    /// </param>
    /// <param name="store">The store of the endpoints, which the connector disposes with itself.</param>
    /// <param name="serverCheckTimeout">How long a check of an endpoint may take, or <see langword="null"/> for 7 seconds.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the connector.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<ManagedProxyConnector> Create(
        ProxyOptions proxyOptions,
        IProxyEndPointStore store,
        TimeSpan? serverCheckTimeout,
        CancellationToken cancellationToken)
    {
        var connector = new ManagedProxyConnector(proxyOptions, store, serverCheckTimeout);

        if (proxyOptions.ResetStates)
            await VhInvoker.TryInvokeAsync("reset the proxy endpoints' statuses", LogLevel.Warning,
                () => store.ResetStatuses(cancellationToken)).Vhc();

        // load the working set from the shared store; the store list is authoritative
        await connector.ReloadEntries(cancellationToken).Vhc();

        // Start auto-update if configured
        connector.RestartAutoUpdate();

        return connector;
    }

    /// <inheritdoc/>
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

    /// <summary>Gets the endpoints the connector holds, with their statuses.</summary>
    /// <returns>The connector's own endpoint and status objects, which later connections update.</returns>
    public IReadOnlyList<ProxyEndPointInfo> GetEndPointInfos() =>
        _proxyEndPointEntries.Select(x => x.Info).ToArray();

    private async Task<ProxyEndPointEntry[]> LoadEntries(CancellationToken cancellationToken)
    {
        var records = await _store.List(cancellationToken).Vhc();
        return records
            .Select(x => new ProxyEndPointEntry(x.ToInfo()))
            .ToArray();
    }

    private async Task ReloadEntries(CancellationToken cancellationToken)
    {
        // dropping the old entries also drops their dirty flags
        _proxyEndPointEntries = await LoadEntries(cancellationToken).Vhc();
        _queuePosition = await _store.GetQueuePosition(cancellationToken).Vhc();
        IsEnabled = _proxyEndPointEntries.Length > 0;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It saves the statuses not saved yet, or, with <see cref="ProxyOptions.ResetStates"/>, clears the stored ones
    /// instead, then loads the endpoints from the store again. The update from a URL starts again, with a download at
    /// once, only when its URL or interval changed.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task UpdateOptions(ProxyOptions proxyOptions, CancellationToken cancellationToken)
    {
        _verifyTls = proxyOptions.VerifyTls;

        if (proxyOptions.ResetStates) {
            // discard in-memory statuses instead of flushing them back over the app's reset
            await VhInvoker.TryInvokeAsync("reset the proxy endpoints' statuses", LogLevel.Warning,
                () => _store.ResetStatuses(cancellationToken)).Vhc();
        }
        else {
            await Flush(cancellationToken).Vhc();
        }

        await ReloadEntries(cancellationToken).Vhc();

        // the job again, with its download at once, only for a new url or interval: an app sends the options
        // at every settings save
        var oldAutoUpdateOptions = _autoUpdateOptions;
        _autoUpdateOptions = proxyOptions.AutoUpdateOptions;
        if (_autoUpdateOptions.Url != oldAutoUpdateOptions.Url ||
            _autoUpdateOptions.Interval != oldAutoUpdateOptions.Interval)
            RestartAutoUpdate();
    }

    private ValueTask FlushJob(CancellationToken cancellationToken)
    {
        return new ValueTask(Flush(cancellationToken));
    }

    /// <inheritdoc/>
    /// <remarks>It saves the changed statuses and enabled states, and the queue position.</remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task Flush(CancellationToken cancellationToken)
    {
        var dirtyEntries = _proxyEndPointEntries.Where(x => x.IsDirty).ToArray();
        if (dirtyEntries.Length == 0)
            return;

        await _store.UpdateStatuses(dirtyEntries.Select(x => x.Info).ToArray(), cancellationToken).Vhc();
        await _store.SetQueuePosition(_queuePosition, cancellationToken).Vhc();
        foreach (var entry in dirtyEntries)
            entry.IsDirty = false;
    }

    // The old job stops, and a new one starts where the options ask for one
    private void RestartAutoUpdate()
    {
        _autoUpdateJob?.Dispose();
        _autoUpdateJob = null;
        if (_autoUpdateOptions.Interval is not { } interval || interval <= TimeSpan.Zero ||
            _autoUpdateOptions.Url == null)
            return;

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
            await Flush(cancellationToken).Vhc();
            await _store.Merge(newEndPoints, _autoUpdateOptions.MaxItemCount, _autoUpdateOptions.MaxPenalty,
                _autoUpdateOptions.RemoveDuplicateIps, cancellationToken).Vhc();
            await ReloadEntries(cancellationToken).Vhc();

            VhLogger.Instance.LogInformation("Downloaded and merged proxy list. Total proxies: {Count}",
                _proxyEndPointEntries.Length);

            // Check servers with the last factory a caller gave us. Before the first connect there is none;
            // GetOrderedEntries then checks the merged endpoints on that connect anyway.
            var socketFactory = _lastSocketFactory;
            if (socketFactory != null)
                await CheckServers(socketFactory, cancellationToken).Vhc();
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

    /// <inheritdoc/>
    /// <remarks>
    /// It checks the enabled endpoints, the best first, and skips the rest of those already rated once 10 have
    /// succeeded; see the class's remarks. What it recorded is saved even when it is canceled. The update from a URL
    /// checks with the last factory a caller gave.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
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

            // what it recorded is kept, a cancelled check's too: not under its token
            await Flush(CancellationToken.None).Vhc();
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
        return [.. result];
    }

    private readonly AsyncLock _connectLock = new();

    private async Task<ProxyEndPointEntry[]> GetOrderedEntries(ISocketFactory socketFactory,
        CancellationToken cancellationToken)
    {
        // lock till get an ordered list with at least one succeeded server
        // if there is a failed server on top, all connection must wait till re-check is done
        using var scopeLock = await _connectLock.LockAsync(cancellationToken).Vhc();
        Interlocked.Increment(ref _queuePosition);

        var endPointEntries = GetOrderedEntriesQuery().ToArray();
        if (endPointEntries.FirstOrDefault()?.Status.IsLastUsedSucceeded == true)
            return endPointEntries;

        // push the failed ones to the end and try again
        await CheckServers(socketFactory, endPointEntries, 1, cancellationToken).Vhc();
        return [.. GetOrderedEntriesQuery()];
    }

    /// <inheritdoc/>
    /// <remarks>
    /// One connection at a time chooses its order, and waits for the check that runs when the first endpoint did not
    /// succeed last. Each success and failure is recorded, and reported to <paramref name="onAttempt"/>; a failure taken
    /// for the caller's cancellation, any <see cref="OperationCanceledException"/> within the check timeout, is
    /// neither, and a cancellation during the last attempt ends in the <see cref="SocketException"/>. With
    /// <see cref="ProxyAutoUpdateOptions.RemoveDuplicateIps"/>, a success disables the other endpoints of its host and
    /// protocol among those the connection could choose. The connector keeps the last socket factory for its checks.
    /// </remarks>
    /// <exception cref="SocketException">No endpoint could connect, with <see cref="SocketError.NetworkUnreachable"/>.</exception>
    /// <exception cref="ObjectDisposedException">The connector was disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<TcpClient> ConnectAsync(ISocketFactory socketFactory, IPEndPoint ipEndPoint,
        Action? onAttempt, CancellationToken cancellationToken)
    {
        _lastSocketFactory = socketFactory;

        // get ordered endpoints
        var entries = await GetOrderedEntries(socketFactory, cancellationToken).Vhc();

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

                // timed no later than the attempt callback, which may restart the caller's timeout
                _handedOutConnections.AddOrUpdate(tcpClient, new HandedOutConnection(entry, Environment.TickCount64));
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

                if (!IsCallerCancellation(ex, delay)) {
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
                _sessionStatus.LastSucceeded = FastDateTime.UtcNow;
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
                _sessionStatus.LastFailed = FastDateTime.UtcNow;
                _sessionStatus.Latency = null;
                _sessionStatus.ErrorMessage = entry.Status.ErrorMessage;
            }
        }
    }

    // let's not assume bad server if caller cancelled the operation: a cancellation within a server check's time
    private bool IsCallerCancellation(Exception ex, TimeSpan elapsed) =>
        ex is OperationCanceledException && elapsed < _serverCheckTimeout;

    /// <inheritdoc/>
    /// <remarks>
    /// It counts once per connection: the endpoint's success is taken back and a failure recorded. A connection it did
    /// not hand out changes nothing; for an <see cref="OperationCanceledException"/> within the check timeout after the
    /// connection was made, the connection is forgotten with no failure recorded.
    /// </remarks>
    public void RecordFailed(TcpClient tcpClient, Exception ex)
    {
        // counted once, against the proxy that made the connection
        if (!_handedOutConnections.TryGetValue(tcpClient, out var handedOut) ||
            !_handedOutConnections.Remove(tcpClient))
            return;

        var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - handedOut.TickCount);
        if (IsCallerCancellation(ex, elapsed))
            return;

        handedOut.Entry.Status.SucceededCount--; // decrement succeeded count as the connection failed
        RecordFailed(handedOut.Entry, ex, checkMode: false);
    }

    /// <summary>
    /// Stops the update and the saving, saves the statuses not saved yet, logging a failure, and disposes the store.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask DisposeAsync()
    {
        // a second close waits for the first one's end, as its flush may still be under way
        using var scopeLock = await _disposeLock.LockAsync(CancellationToken.None).Vhc();
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        _autoUpdateJob?.Dispose();
        _flushJob.Dispose();

        // persist pending statuses to the shared store; a failed flush must not keep the store open
        try {
            await Flush(CancellationToken.None).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not save the proxy endpoint statuses.");
        }

        _store.Dispose();
    }
}
