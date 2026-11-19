using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
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
/// the endpoints are checked first against the connection's own server, until one succeeds. The connections to one
/// server wait for its one check; those to other servers check beside it, as a server that is down takes the check's
/// whole time.
/// </para>
/// <para>
/// A check connects through each enabled endpoint, up to 50 at a time, to the VPN server's endpoint the caller gives,
/// with <see cref="ProxyOptions.VerifyTls"/> also through a TLS handshake with it, whose certificate the caller's check
/// takes, the VPN connection's own; no certificate is downloaded for its chain. It records each outcome. The device
/// reaches no one else through the proxies. Once 10 have succeeded, it checks only those never rated. A SOCKS endpoint
/// whose check fails with a <see cref="VpnHood.Net.Proxies.ProxyClientException"/> of
/// <see cref="System.Net.Sockets.SocketError.ProtocolNotSupported"/>, as one that is no proxy of its kind, is disabled;
/// any other failure, which may be the server's, is recorded alone, as is that one of an HTTP or HTTPS endpoint, which
/// also stands for its refusal of the server, as of a port other than 443. The statuses are saved to the store every
/// 10 seconds, after a check, and at the disposal.
/// </para>
/// </remarks>
public class ManagedProxyConnector : IProxyConnector
{
    private ProxyEndPointEntry? _fastestEntry;
    private long _queuePosition;
    private readonly TimeSpan _serverCheckTimeout;
    private readonly IProxyEndPointStore _store;
    // the most recent protected factory and VPN server handed to us by a caller; the auto-update job has no caller of
    // its own, so it reuses these and simply skips the check before the first connect. The server is one object, so the
    // job reads one caller's whole
    private volatile ISocketFactory? _lastSocketFactory;
    private volatile CheckTarget? _lastTarget;
    // one check of a server at a time, which the connections to it wait for
    private readonly ConcurrentDictionary<IPEndPoint, AsyncLock> _checkLocks = new();
    private ProxyEndPointEntry[] _proxyEndPointEntries;
    private ProgressMonitor? _progressMonitor;
    private Job? _autoUpdateJob;
    private readonly Job _flushJob;
    private ProxyAutoUpdateOptions _autoUpdateOptions;
    private bool _disposed;
    private readonly AsyncLock _disposeLock = new();
    private readonly AsyncLock _flushLock = new();
    private bool _verifyTls;
    private readonly DateTime _sessionCreatedTime = FastDateTime.UtcNow;
    private readonly ProxySessionStatus _sessionStatus = new();
    // the proxy that made each connection handed out, for a failure after its connect, as on TLS; weak, as most
    // connections never fail
    private readonly ConditionalWeakTable<TcpClient, HandedOutConnection> _handedOutConnections = new();
    private const int CheckServerMaxDegreeOfParallelism = 50;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(10);

    // a VPN server the proxies are checked against: its endpoint, the name its TLS takes, and the check of its certificate
    private sealed record CheckTarget(
        IPEndPoint EndPoint,
        string? HostName,
        RemoteCertificateValidationCallback? CertificateValidation);

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
                    IsAnySucceeded = _proxyEndPointEntries.Any(x => x.EndPoint.IsEnabled && x.IsLastUsedAnswered),
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

        // no flush between the save and the reload, whose entries would not have its changes
        using (await _flushLock.LockAsync(cancellationToken).Vhc()) {
            if (proxyOptions.ResetStates) {
                // discard in-memory statuses instead of flushing them back over the app's reset
                await VhInvoker.TryInvokeAsync("reset the proxy endpoints' statuses", LogLevel.Warning,
                    () => _store.ResetStatuses(cancellationToken)).Vhc();
            }
            else {
                await FlushCore(cancellationToken).Vhc();
            }

            await ReloadEntries(cancellationToken).Vhc();
        }

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
    /// <remarks>
    /// It saves the changed statuses and enabled states, and the queue position, one flush at a time. A change made while
    /// it writes is saved by the next flush, and a failed write leaves its changes for the next one too.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task Flush(CancellationToken cancellationToken)
    {
        // one at a time: an earlier flush's write that ended after a later one's put back the older statuses
        using var scopeLock = await _flushLock.LockAsync(cancellationToken).Vhc();
        await FlushCore(cancellationToken).Vhc();
    }

    // the caller holds the flush lock
    private async Task FlushCore(CancellationToken cancellationToken)
    {
        var dirtyEntries = _proxyEndPointEntries.Where(x => x.IsDirty).ToArray();
        if (dirtyEntries.Length == 0)
            return;

        // copies, which clear the flags, before the write: cleared after it, they took with them a change made during it
        var infos = dirtyEntries.Select(x => x.TakeForFlush()).ToArray();
        try {
            await _store.UpdateStatuses(infos, cancellationToken).Vhc();
            await _store.SetQueuePosition(_queuePosition, cancellationToken).Vhc();
        }
        catch {
            foreach (var entry in dirtyEntries)
                entry.IsDirty = true;
            throw;
        }
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

            // merge into the shared store and reload the working set with preserved statuses, with no flush between
            using (await _flushLock.LockAsync(cancellationToken).Vhc()) {
                await FlushCore(cancellationToken).Vhc();
                await _store.Merge(newEndPoints, _autoUpdateOptions.MaxItemCount, _autoUpdateOptions.MaxPenalty,
                    _autoUpdateOptions.RemoveDuplicateIps, cancellationToken).Vhc();

                await ReloadEntries(cancellationToken).Vhc();
            }

            VhLogger.Instance.LogInformation("Downloaded and merged proxy list. Total proxies: {Count}",
                _proxyEndPointEntries.Length);

            // Check servers with the last factory and server a caller gave us. Before the first connect there are none;
            // GetOrderedEntries then checks the merged endpoints on that connect anyway.
            if (_lastSocketFactory is { } socketFactory && _lastTarget is { } target)
                await CheckServers(socketFactory, target.EndPoint, target.HostName, target.CertificateValidation,
                    cancellationToken).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Failed to update proxy list from {Url}", _autoUpdateOptions.Url);
        }
    }

    // Connects through the proxy to the VPN server and, with VerifyTls, completes a TLS handshake with it, as the VPN's
    // own connection would: a proxy that opens the tunnel and passes nothing fails here, and one that stands in for the
    // server with a certificate of its own fails the server's check
    private async Task<TimeSpan> CheckConnectionAsync(IProxyClient proxyClient, TcpClient tcpClient,
        CheckTarget target, ProgressMonitor? progressMonitor, CancellationToken cancellationToken)
    {
        using var serverCheckCts = new CancellationTokenSource(_serverCheckTimeout);
        try {
            var tickCount = Environment.TickCount64;
            using var linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, serverCheckCts.Token);
            await proxyClient.ConnectAsync(tcpClient, target.EndPoint, linkedCts.Token).Vhc();
            var latency = TimeSpan.FromMilliseconds(Environment.TickCount64 - tickCount);

            // the proxy's own stream, an HTTPS proxy's TLS session, which the socket's stream passes by; closed here,
            // a failed check's too, as the caller closes the socket alone
            var stream = ProxyClientFactory.GetConnection(proxyClient, tcpClient).Stream;
            try {
                if (_verifyTls) {
                    await using var sslStream = new SslStream(stream, leaveInnerStreamOpen: true);
                    await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
                        TargetHost = target.HostName ?? target.EndPoint.Address.ToString(),
                        ApplicationProtocols = [SslApplicationProtocol.Http11],
                        RemoteCertificateValidationCallback = target.CertificateValidation ?? ((_, _, _, _) => true),
                        // nothing fetched for the chain: a certificate a proxy shows would name the address the device
                        // asks, outside the tunnel
                        CertificateChainPolicy = new X509ChainPolicy {
                            DisableCertificateDownloads = true,
                            RevocationMode = X509RevocationMode.NoCheck
                        }
                    }, linkedCts.Token).Vhc();
                }
            }
            finally {
                if (stream is not NetworkStream)
                    await stream.TryDisposeAsync(LogLevel.Warning).Vhc();
            }

            return latency;
        }
        catch (OperationCanceledException ex) when (serverCheckCts.IsCancellationRequested) {
            throw new TimeoutException("Connection check timed out.", ex);
        }
        finally {
            progressMonitor?.IncrementCompleted();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It checks the enabled endpoints, the best first, and skips the rest of those already rated once 10 have
    /// succeeded; see the class's remarks. What it recorded is saved even when it is canceled. The update from a URL
    /// checks with the last factory and server a caller gave.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public Task CheckServers(ISocketFactory socketFactory, IPEndPoint ipEndPoint, string? hostName,
        RemoteCertificateValidationCallback? serverCertificateValidation, CancellationToken cancellationToken)
    {
        var target = new CheckTarget(ipEndPoint, hostName, serverCertificateValidation);
        _lastSocketFactory = socketFactory;
        _lastTarget = target;

        const int satisfiedSuccessCount = 10;
        var endpoints = _proxyEndPointEntries
            .Where(x => x.EndPoint.IsEnabled)
            .OrderBy(x => x.Status.Quality)
            .ToArray();

        return CheckServers(socketFactory, target, endpoints, satisfiedSuccessCount, stopWhenSatisfied: false,
            cancellationToken);
    }

    // With stopWhenSatisfied, the run ends at its successes: the checks under way end, and count for nothing
    private async Task CheckServers(ISocketFactory socketFactory,
        CheckTarget target,
        IEnumerable<ProxyEndPointEntry> endpoints,
        int satisfiedSuccessCount,
        bool stopWhenSatisfied,
        CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("Checking proxy servers for reachability...");

        // initialize progress tracker
        _progressMonitor = new ProgressMonitor(
            totalTaskCount: _proxyEndPointEntries.Length,
            taskTimeout: _serverCheckTimeout,
            maxDegreeOfParallelism: CheckServerMaxDegreeOfParallelism);

        try {
            // concurrent results container: the outcomes alone, as each check closes its own socket, and whether the
            // run was canceled already when the check failed
            var results =
                new ConcurrentBag<(ProxyEndPointEntry Entry, TimeSpan? Latency, Exception? Error, bool IsCanceled)>();

            using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var parallelOptions = new ParallelOptions {
                CancellationToken = runCts.Token,
                MaxDegreeOfParallelism = CheckServerMaxDegreeOfParallelism
            };

            // check all endpoints in parallel. If enough good servers found, stop checking more.
            var successCount = 0;
            try {
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
                        var latency = await CheckConnectionAsync(proxyClient, tcpClient, target, _progressMonitor, ct).Vhc();
                        results.Add((entry, latency, null, false));
                        if (Interlocked.Increment(ref successCount) >= satisfiedSuccessCount && stopWhenSatisfied)
                            await runCts.CancelAsync().Vhc();
                    }
                    catch (Exception ex) {
                        results.Add((entry, null, ex, ct.IsCancellationRequested));
                    }
                    finally {
                        // here, whatever ends the run: a canceled run left its sockets open
                        tcpClient?.Dispose();
                    }
                }).Vhc();
            }
            catch (OperationCanceledException) when (runCts.IsCancellationRequested &&
                                                     !cancellationToken.IsCancellationRequested) {
                // the run ended at its successes
            }
            finally {
                // the outcomes of the checks that ended, a canceled run's too
                RecordCheckResults(results);
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


    private void RecordCheckResults(
        IReadOnlyCollection<(ProxyEndPointEntry Entry, TimeSpan? Latency, Exception? Error, bool IsCanceled)> results)
    {
        // find the fastest server and set threshold
        var fastestLatency = results
            .Where(x => x.Error == null && x.Entry.EndPoint.IsEnabled && x.Latency.HasValue)
            .Select(x => x.Latency)
            .Min();

        foreach (var (entry, latency, error, isCanceled) in results) {
            // record success
            if (error is null) {
                RecordSuccess(entry,
                    latency: latency ?? throw new InvalidOperationException("A check that passed has no latency."),
                    fastestLatency: fastestLatency, checkMode: true);

                continue;
            }

            // a check that failed once the run was canceled is no failure of its proxy: the cancellation may have ended
            // it; one that failed before is
            if (isCanceled)
                continue;

            RecordFailed(entry, error, checkMode: true);

            // Disabled only where it is no proxy of its kind, an answer the server has no part in: a refusal, as SOCKS4's
            // "rejected or failed" is, may be for a server that is down, and a TLS failure may be the server's. An HTTP
            // proxy's refusal of the server, as a 403 for a port other than 443, comes as the same error as an answer
            // that is no HTTP at all, so an HTTP or HTTPS endpoint is never disabled here
            var isProtocolRejected = entry.EndPoint.Protocol is ProxyProtocol.Socks4 or ProxyProtocol.Socks5 &&
                                     error is ProxyClientException {
                                         SocketErrorCode: SocketError.ProtocolNotSupported
                                     };

            if (isProtocolRejected) {
                entry.EndPoint.IsEnabled = false;
                entry.IsDirty = true;
            }
        }
    }

    // order by active state then Last Attempt
    private IReadOnlyList<ProxyEndPointEntry> GetOrderedEntriesQuery(int maxPriorityFailed = 1)
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
        return result;
    }

    private async Task<IReadOnlyList<ProxyEndPointEntry>> GetOrderedEntries(ISocketFactory socketFactory,
        CheckTarget target,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _queuePosition);
        var endPointEntries = GetOrderedEntriesQuery();
        if (endPointEntries.FirstOrDefault()?.Status.IsLastUsedSucceeded == true)
            return endPointEntries;

        // The connections to this server wait for one check, which one of them may have run meanwhile; those to other
        // servers check beside it, as one that is down takes the check's whole time
        var checkLock = _checkLocks.GetOrAdd(target.EndPoint, _ => new AsyncLock());
        using var scopeLock = await checkLock.LockAsync(cancellationToken).Vhc();
        endPointEntries = GetOrderedEntriesQuery();
        if (endPointEntries.FirstOrDefault()?.Status.IsLastUsedSucceeded == true)
            return endPointEntries;

        // Push the failed ones to the end and try again, against this connection's server, until one succeeds: a check
        // against another, which may be down, rated them all failed. Where only the endpoints that succeeded lately may
        // be taken and none has, all the enabled ones are checked
        var entriesToCheck = endPointEntries.Count > 0
            ? endPointEntries
            : _proxyEndPointEntries.Where(x => x.EndPoint.IsEnabled).ToArray();

        await CheckServers(socketFactory, target, entriesToCheck, satisfiedSuccessCount: 1, stopWhenSatisfied: true,
            cancellationToken).Vhc();

        // the connection takes an endpoint that has just passed, before those the check ended unrated, which sort first
        endPointEntries = GetOrderedEntriesQuery();
        return [
            .. endPointEntries.Where(x => x.Status.IsLastUsedSucceeded),
            .. endPointEntries.Where(x => !x.Status.IsLastUsedSucceeded)
        ];
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A connection waits for the check of its server that runs when the first endpoint did not succeed last, which
    /// takes the certificate <paramref name="serverCertificateValidation"/> takes. Each success and failure is recorded,
    /// and reported to <paramref name="onAttempt"/>; a failure taken
    /// for the caller's cancellation, any <see cref="OperationCanceledException"/> within the check timeout, is
    /// neither, and a cancellation of <paramref name="cancellationToken"/> during the last attempt is thrown. With
    /// <see cref="ProxyAutoUpdateOptions.RemoveDuplicateIps"/>, a success disables the other endpoints of its host and
    /// protocol among those the connection could choose. The connector keeps the last socket factory for its checks.
    /// </remarks>
    /// <exception cref="SocketException">No endpoint could connect, with <see cref="SocketError.NetworkUnreachable"/>.</exception>
    /// <exception cref="ObjectDisposedException">The connector was disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<ProxyConnection> ConnectAsync(ISocketFactory socketFactory, IPEndPoint ipEndPoint,
        string? hostName, RemoteCertificateValidationCallback? serverCertificateValidation, Action? onAttempt,
        CancellationToken cancellationToken)
    {
        var target = new CheckTarget(ipEndPoint, hostName, serverCertificateValidation);
        _lastSocketFactory = socketFactory;
        _lastTarget = target;

        // get ordered endpoints
        var entries = await GetOrderedEntries(socketFactory, target, cancellationToken).Vhc();

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

                return ProxyClientFactory.GetConnection(proxyClient, tcpClient);
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

        // could not connect to any proxy server; a cancellation that ended the last try is the caller's
        cancellationToken.ThrowIfCancellationRequested();
        throw new SocketException((int)SocketError.NetworkUnreachable);
    }

    private void RecordSuccess(ProxyEndPointEntry entry, TimeSpan latency, TimeSpan? fastestLatency,
        bool checkMode)
    {
        entry.RecordSuccess(latency, fastestLatency, _queuePosition);
        if (!checkMode) {
            lock (_sessionStatus) {
                _sessionStatus.SucceededCount++;
                _sessionStatus.LastSucceeded = DateTime.UtcNow; // once per connection: the coarse second made ties
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

    // let's not assume bad server if caller cancelled the operation: a cancellation within a server check's time
    private bool IsCallerCancellation(Exception ex, TimeSpan elapsed) =>
        ex is OperationCanceledException && elapsed < _serverCheckTimeout;

    /// <inheritdoc/>
    /// <remarks>
    /// It counts once per connection: the endpoint's success is taken back and a failure recorded. A connection it did
    /// not hand out changes nothing; an <see cref="OperationCanceledException"/> within the check timeout after the
    /// connection was made records nothing, and leaves the connection for a failure that comes after it.
    /// </remarks>
    public void RecordFailed(TcpClient tcpClient, Exception ex)
    {
        if (!_handedOutConnections.TryGetValue(tcpClient, out var handedOut))
            return;

        // a cancellation is no failure, and leaves the connection known for one that comes after it
        var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - handedOut.TickCount);
        if (IsCallerCancellation(ex, elapsed))
            return;

        // counted once, against the proxy that made the connection
        if (!_handedOutConnections.Remove(tcpClient))
            return;

        // the endpoint as the connector holds it now: a reload since the handout made its object anew, and the old one
        // is saved no more. Found by its address's parts, which its Id hashes
        var handedOutEndPoint = handedOut.Entry.EndPoint;
        var entries = _proxyEndPointEntries;
        var entry = Array.Find(entries, x => ReferenceEquals(x, handedOut.Entry)) ??
                    Array.Find(entries, x =>
                        x.EndPoint.Protocol == handedOutEndPoint.Protocol &&
                        x.EndPoint.Port == handedOutEndPoint.Port &&
                        x.EndPoint.Host == handedOutEndPoint.Host) ??
                    handedOut.Entry;

        entry.TakeBackSuccess(); // its success is taken back, as its connection failed
        RecordFailed(entry, ex, checkMode: false);
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
