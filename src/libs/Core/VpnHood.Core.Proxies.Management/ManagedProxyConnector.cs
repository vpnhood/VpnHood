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
/// A connection tries the enabled endpoints that succeeded last first, then the rest, each group in turn, the one whose
/// turn comes soonest first: an endpoint's next turn moves back by 3 for each point of its penalty; where all those
/// fail, it tries the ones that passed a check since it took them; it skips one turned off or dropped since. Where none
/// it may take succeeded last, the endpoints are checked first against the connection's own server, until one
/// succeeds. The connections to one server wait for its one check; those to other servers check beside it, as a server
/// that is down takes the check's whole time. A connection made while no endpoint is enabled waits for the first
/// download of the list's update, and fails where it brings none: a connection goes through a proxy or not at all.
/// </para>
/// <para>
/// A check connects through each enabled endpoint, up to 50 at a time, to the VPN server's endpoint the caller gives,
/// with <see cref="ProxyOptions.VerifyTls"/> also through a TLS handshake with it, whose certificate the caller's check
/// takes, the VPN connection's own; no certificate is downloaded for its chain. It records each outcome as it comes; a
/// check that a pass elsewhere stopped records nothing, and one of an endpoint turned off or gone meanwhile is no pass.
/// The device reaches no one else through the proxies. A check before a connect
/// (<see cref="CheckServers(ISocketFactory, IPEndPoint, string?, RemoteCertificateValidationCallback?, CancellationToken)"/>)
/// lets the connect go on at the first endpoint that passes, those that succeeded last first, and goes on behind it as
/// the one that follows a list's update does: through every enabled endpoint, and once 10 have succeeded, only those
/// never rated, then through those never rated that a reload brought meanwhile. The next check before a connect stops
/// it. A connection's own check ends at its first pass, and what it left is then checked behind it in the same way,
/// where no such check is under way; so are the endpoints a list's first download brings, or the app adds or turns on,
/// after such a check ended, unless a connection waits for the list, and a later download's check of the whole list
/// takes its place, so the next one stops it. On
/// iOS and tvOS the checks beside the session make TLS handshakes 3 at a time, an HTTPS proxy's own among them: every
/// check that comes once a proxy reached a server, and those nobody waits for from their start. The connections' own
/// checks have 3 places of their own, so a connection never waits behind a check nobody waits for, whose checks may
/// hold theirs for their whole time. They dial as many at once as elsewhere: such a check dials its proxy alone first,
/// and closes that connection at once, so a proxy that never answers takes no place, and waits for its place with
/// nothing open, which the proxy or the server would close. The close stops every check and a connection's dial, and
/// waits for the checks before its save. A SOCKS endpoint
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
    // The factory and VPN server of the last check through which a proxy reached the server, the VPN's TLS with
    // VerifyTls, which the checks behind the session take, as they have no caller of their own. Not the last call's: a
    // server search's probe of a server that is down would rate every proxy they check failed. Where no check passed,
    // the first connection's that reached its server, whose TLS comes after it: a key known by name alone may never
    // check in a session; that connection's failure takes it back. One object, so a reader takes one's whole
    private volatile LastCheck? _lastCheck;

    // the connections waiting for the list's endpoints, which check what a reload brings themselves
    private int _endPointWaiterCount;
    // one check of a server at a time, which the connections to it wait for
    private readonly ConcurrentDictionary<IPEndPoint, AsyncLock> _checkLocks = new();
    private readonly Lock _duplicateLock = new();
    private ProxyEndPointEntry[] _proxyEndPointEntries;
    private ProgressMonitor? _progressMonitor;
    // completed, and replaced, at each reload of the entries, which may bring one a waiting connection can use
    private TaskCompletionSource _entriesReload = new(TaskCreationOptions.RunContinuationsAsynchronously);
    // the job of the list's update, made, replaced and closed under its lock
    private readonly Lock _autoUpdateLock = new();
    private Job? _autoUpdateJob;
    // the job's first download, which a connection waits for while no endpoint is enabled; under the job's lock too
    private TaskCompletionSource? _firstListUpdate;
    // one run of the update at a time, which the close waits for before it closes the store
    private readonly AsyncLock _listUpdateLock = new();
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
    // the statuses' resets by new options, which drop the successes the handed-out connections counted
    private long _statusResetCount;
    // The checks under way, counted under their lock, which the close stops at once and waits for before its save. The
    // check before a connect, which goes on after its first pass, has a stop of its own too, which the next one gives.
    // Neither stop is disposed: they have no timer, and a stop may come at any time
    private readonly Lock _checkLock = new();
    private readonly CancellationTokenSource _closeCts = new();
    private int _checkCount;
    private TaskCompletionSource? _checksEnded;
    private CancellationTokenSource? _connectCheckStop;
    private const int CheckServerMaxDegreeOfParallelism = 50;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(10);
    // a connect waits for a job's first download, so a list's host that does not answer may not hold it for long
    private static readonly TimeSpan FirstListDownloadTimeout = TimeSpan.FromSeconds(30);

    // a VPN server the proxies are checked against: its endpoint, the name its TLS takes, and the check of its certificate
    private sealed record CheckTarget(
        IPEndPoint EndPoint,
        string? HostName,
        RemoteCertificateValidationCallback? CertificateValidation);

    // the factory and server of a check through which a proxy reached the server, or of the connection that did
    private sealed record LastCheck(ISocketFactory SocketFactory, CheckTarget Target, TcpClient? Connection = null);

    /// <inheritdoc/>
    /// <value><see langword="true"/> while the store has any endpoint, as last loaded.</value>
    public bool IsEnabled { get; private set; }

    /// <inheritdoc/>
    public bool UseRecentSucceeded { get; set; }

    /// <inheritdoc/>
    public ProgressStatus? Progress => _progressMonitor?.Progress;

    // how many endpoints a check dials at once, one beside the session too; a test's to change
    internal int CheckWidth { get; set; } = CheckServerMaxDegreeOfParallelism;

    // The places for the TLS handshakes of the checks beside the session that nobody waits for, which they share: the
    // check behind a connect once a pass came, and the list's and the app's. On iOS and tvOS 3, as the tunnel's own
    // connects there (ConnectorService's gate): each handshake costs native memory outside every managed budget, against
    // an extension's limit of about 50 MB. A dial is no handshake, so a list whose proxies mostly never answer goes as
    // fast as elsewhere. Null elsewhere, where the dials' width is the only one. A test's to change; the close disposes it
    internal SemaphoreSlim? HandshakePlaces { get; set; } = CreateHandshakePlaces();

    // The places for the TLS handshakes of the connections' own checks beside the session, which they share: apart from
    // those of the checks nobody waits for, which may hold theirs for a check's whole time while a connection waits. On
    // iOS and tvOS 3, so 6 at most in all. A test's to change; the close disposes it
    internal SemaphoreSlim? ConnectionHandshakePlaces { get; set; } = CreateHandshakePlaces();

    private static SemaphoreSlim? CreateHandshakePlaces() =>
        (OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst()) || OperatingSystem.IsTvOS()
            ? new SemaphoreSlim(3)
            : null;

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
    /// <see cref="Status"/> hold from the start, and starts its update from a URL where the options ask for one: its
    /// first download at once, and, with no interval, that one alone where no endpoint is enabled.
    /// </summary>
    /// <param name="proxyOptions">
    /// The options; with <see cref="ProxyOptions.ResetStates"/>, the stored statuses are cleared first, which logs a
    /// failure rather than throwing it.
    /// </param>
    /// <param name="store">The store of the endpoints, which the connector disposes with itself.</param>
    /// <param name="socketFactory">
    /// The factory of the sockets of the first download of the update this call starts, which it starts at once, off
    /// the caller's thread, before the connect's tunnel is up: its sockets may still open after the tunnel is up, where
    /// the connect does not wait for it, as it does while no endpoint is enabled. The list's host name is asked of the
    /// network's resolvers over them, as under a kill switch that lets only the VPN's own traffic out. Or <see langword="null"/> for the system's sockets and resolver. The update's later downloads,
    /// and those of an update that <see cref="UpdateOptions"/> starts, take the system's in any case, which the platform
    /// routes.
    /// </param>
    /// <param name="serverCheckTimeout">How long a check of an endpoint may take, or <see langword="null"/> for 7 seconds.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the connector.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<ManagedProxyConnector> Create(
        ProxyOptions proxyOptions,
        IProxyEndPointStore store,
        ISocketFactory? socketFactory,
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
        connector.RestartAutoUpdate(socketFactory);

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
        var statusResetCount = Interlocked.Read(ref _statusResetCount);
        return records
            .Select(x => new ProxyEndPointEntry(x.ToInfo()) { StatusResetCount = statusResetCount })
            .ToArray();
    }

    private async Task ReloadEntries(CancellationToken cancellationToken)
    {
        // dropping the old entries also drops their dirty flags
        _proxyEndPointEntries = await LoadEntries(cancellationToken).Vhc();
        _queuePosition = await _store.GetQueuePosition(cancellationToken).Vhc();
        IsEnabled = _proxyEndPointEntries.Length > 0;

        // the connections waiting for an enabled endpoint look again
        Interlocked.Exchange(ref _entriesReload,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It saves the statuses not saved yet, or, with <see cref="ProxyOptions.ResetStates"/>, clears the stored counts and
    /// saves the enabled states not saved yet alone, which the reset keeps: the app resets the store before it asks,
    /// and no count held is written back over it. A failed reset, which it logs, changes nothing, and the statuses are
    /// saved as without one. It then loads the endpoints from the store again; where that fails after a reset, it clears
    /// the counts it holds too. Endpoints the reload brought or turned on that no check rated, as the app's, are checked
    /// behind the session against the last server a check reached, unless a connection waits for the list, which checks
    /// them itself. The update from a URL starts again, with a download at
    /// once, when its URL or interval changed, or where the reload takes the last enabled endpoint, or leaves none with
    /// nothing to download the list, but not while the list's first download is still coming. A connection waiting for
    /// the list goes on where the reload brings an enabled endpoint. The downloads of the new update take the system's
    /// sockets, which the platform routes, through the tunnel where it is up.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The connector was disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task UpdateOptions(ProxyOptions proxyOptions, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _verifyTls = proxyOptions.VerifyTls;

        // no flush between the save and the reload, whose entries would not have its changes. The close's own save
        // takes this lock, and closes the store after it
        var hadEnabledEndPoint = HasEnabledEndPoint;
        IReadOnlySet<(ProxyProtocol, string, int)> enabledBefore;
        bool isWaitedFor;
        using (await _flushLock.LockAsync(cancellationToken).Vhc()) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            enabledBefore = GetEnabledAddresses();
            isWaitedFor = Volatile.Read(ref _endPointWaiterCount) > 0;

            // The app resets the shared store before it asks for this, so no count held is written back over the reset:
            // the enabled states alone, which the reset keeps, go to the store after it. A failed reset changes nothing,
            // and the statuses are saved as without one
            var isReset = proxyOptions.ResetStates &&
                          await VhInvoker.TryInvokeAsync("reset the proxy endpoints' statuses", LogLevel.Warning,
                              ResetStatuses, defaultValue: false).Vhc();

            // Counted before the reload, whose entries carry the count: a connection that took a success before them
            // takes none back. The held entries stay until the reload's swap, so a success during its read lands on an
            // entry it drops, and no later success pays for it; where the save or the read fails before the swap, they
            // are cleared as the store's are, rather than written back over the reset
            if (isReset)
                Interlocked.Increment(ref _statusResetCount);
            else
                await FlushCore(cancellationToken).Vhc();

            var heldEntries = _proxyEndPointEntries;
            try {
                if (isReset)
                    await SaveEnabledStates(cancellationToken).Vhc();

                await ReloadEntries(cancellationToken).Vhc();
            }
            catch when (isReset) {
                if (_proxyEndPointEntries == heldEntries)
                    ResetHeldStatuses();

                throw;
            }
        }

        // endpoints the app added or turned on, which no check took
        StartAddedCheck(enabledBefore, isWaitedFor);

        // The job again, with its download at once, for a new url or interval alone, as an app sends the options at
        // every settings save; or where the reload took the last enabled endpoint, or left none with no job to download
        // the list, as for a list with no interval, whose one download ran already. A first download still coming for
        // the same list goes on: a new one in its place would only start it again
        bool isFirstDownloadPending;
        lock (_autoUpdateLock)
            isFirstDownloadPending = _firstListUpdate is { Task.IsCompleted: false };

        var oldAutoUpdateOptions = _autoUpdateOptions;
        _autoUpdateOptions = proxyOptions.AutoUpdateOptions;
        if (_autoUpdateOptions.Url != oldAutoUpdateOptions.Url ||
            _autoUpdateOptions.Interval != oldAutoUpdateOptions.Interval ||
            (_autoUpdateOptions.Url != null && !HasEnabledEndPoint && !isFirstDownloadPending &&
             (hadEnabledEndPoint || _autoUpdateJob == null)))
            RestartAutoUpdate(firstRunSocketFactory: null);

        async Task<bool> ResetStatuses()
        {
            await _store.ResetStatuses(cancellationToken).Vhc();
            return true;
        }
    }

    // The enabled states not saved yet, as a duplicate's success turned an endpoint off, with the counts a reset of the
    // statuses cleared: the caller holds the flush lock
    private async Task SaveEnabledStates(CancellationToken cancellationToken)
    {
        var dirtyEntries = _proxyEndPointEntries.Where(x => x.IsDirty).ToArray();
        if (dirtyEntries.Length == 0)
            return;

        var infos = dirtyEntries.Select(x => x.TakeEnabledStateForReset()).ToArray();
        try {
            await _store.UpdateStatuses(infos, cancellationToken).Vhc();
        }
        catch {
            // left for the next save, as a failed flush leaves its changes: an endpoint turned off stays off
            foreach (var entry in dirtyEntries)
                entry.IsDirty = true;

            throw;
        }
    }

    private HashSet<(ProxyProtocol, string, int)> GetEnabledAddresses() => _proxyEndPointEntries
        .Where(x => x.EndPoint.IsEnabled)
        .Select(GetAddress)
        .ToHashSet();

    // The held entries as the store's reset left them, where the reload after it failed: the counts cleared, and the
    // enabled states, which the reset keeps, as they are
    private void ResetHeldStatuses()
    {
        // one whose enabled state a failed save left unsaved stays marked for the next save
        var statusResetCount = Interlocked.Read(ref _statusResetCount);
        _proxyEndPointEntries = _proxyEndPointEntries
            .Select(x => new ProxyEndPointEntry(new ProxyEndPointInfo {
                EndPoint = x.CopyEndPoint(),
                Status = new ProxyEndPointStatus()
            }) { StatusResetCount = statusResetCount, IsDirty = x.IsDirty })
            .ToArray();

        _queuePosition = 0;
    }

    private ValueTask FlushJob(CancellationToken cancellationToken)
    {
        return new ValueTask(Flush(cancellationToken));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It saves the changed statuses and enabled states, and the queue position, one flush at a time. A change made while
    /// it writes is saved by the next flush, and a failed write leaves its changes for the next one too. Once the
    /// connector is closed, it saves nothing: the close saved, and closed the store.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task Flush(CancellationToken cancellationToken)
    {
        // one at a time: an earlier flush's write that ended after a later one's put back the older statuses
        using var scopeLock = await _flushLock.LockAsync(cancellationToken).Vhc();
        if (!_disposed)
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

    private bool HasEnabledEndPoint => _proxyEndPointEntries.Any(x => x.EndPoint.IsEnabled);

    // The old job stops, and a new one starts where the options ask for one: its first download at once rather than at
    // the job runner's next check, up to 10 seconds away, as a connection waits for it while no endpoint is enabled; then
    // one every interval. With no interval, as the app's "never", the first download alone, and only while no endpoint
    // is enabled: the app leaves that one to the connector. The new job is in place before the old one stops, so the old
    // one's waiters, which its stop wakes, find the new one and wait for it. The first download goes over the factory
    // given, Create's, as the connect's tunnel is not up yet; new options come once the session is
    private void RestartAutoUpdate(ISocketFactory? firstRunSocketFactory)
    {
        lock (_autoUpdateLock) {
            if (_disposed)
                return;

            var oldAutoUpdateJob = _autoUpdateJob;
            var oldFirstListUpdate = _firstListUpdate;
            var interval = _autoUpdateOptions.Interval is { } value && value > TimeSpan.Zero ? value : (TimeSpan?)null;
            if (_autoUpdateOptions.Url is not { } url || (interval == null && HasEnabledEndPoint)) {
                _autoUpdateJob = null;
                _firstListUpdate = null;
            }
            else {
                // a job's runs come one at a time, the first by RunFirstListUpdate before the job starts. Its URL is its
                // own: an old job's run under way when new options come takes none of theirs
                var runCount = 0;
                var firstListUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var autoUpdateJob = new Job(
                    cancellationToken => UpdateFromUrlAsync(url, isFirstRun: ++runCount == 1, firstRunSocketFactory,
                        firstListUpdate, cancellationToken),

                    new JobOptions {
                        Interval = interval ?? TimeSpan.MaxValue,
                        DueTime = interval ?? TimeSpan.MaxValue,
                        Name = "ProxyEndPointAutoUpdate",
                        AutoStart = false
                    });

                _autoUpdateJob = autoUpdateJob;
                _firstListUpdate = firstListUpdate;

                // off the caller's thread: an HTTP client's start can block there, as on a system proxy's detection
                _ = Task.Run(() => RunFirstListUpdate(autoUpdateJob, firstListUpdate, isPeriodic: interval != null));
            }

            oldAutoUpdateJob?.Dispose();
            oldFirstListUpdate?.TrySetResult();
        }
    }

    // The job runs once now, and its waiters go on at the run's end; a periodic one then starts, an interval from that
    // run's end, unless it was replaced or closed
    private async Task RunFirstListUpdate(Job autoUpdateJob, TaskCompletionSource firstListUpdate, bool isPeriodic)
    {
        try {
            await autoUpdateJob.RunNow().Vhc();
        }
        catch (Exception ex) {
            // a job replaced or closed before its run
            VhLogger.Instance.LogDebug(ex, "The proxy list's first download did not run.");
        }
        finally {
            firstListUpdate.TrySetResult();
        }

        // the close and a restart take the job out of the field, under this lock, before they stop it
        lock (_autoUpdateLock) {
            if (isPeriodic && _autoUpdateJob == autoUpdateJob)
                autoUpdateJob.Start();
        }
    }

    // A connection waiting for the list goes on once it is merged, as the reload wakes it, and the first download is
    // over for new options then: they would otherwise find it still coming, and start no other where the user turned
    // off the endpoints it brought. A job's first run checks nothing beside the connect it was made for, which checks
    // the list before its server search, as do its connections, each against its own server until one passes: the
    // check before the connect takes what it brought, or, where that check ended before it, a check of its own takes
    // those never rated, behind the session. A later run checks the whole list against the last server a proxy reached,
    // in the place of the check before a connect, whose next call stops it; where that check is under way, it takes those
    // never rated that the run brought
    private async ValueTask UpdateFromUrlAsync(Uri url, bool isFirstRun, ISocketFactory? firstRunSocketFactory,
        TaskCompletionSource firstListUpdate, CancellationToken cancellationToken)
    {
        try {
            using var listUpdateScope = await _listUpdateLock.LockAsync(cancellationToken).Vhc();
            VhLogger.Instance.LogInformation("Downloading proxy list from {Url}...", url);

            // The first run, which a connect may wait for, over the factory given for it and for 30 seconds at most. The
            // later runs go the platform's way, as the system's sockets do, with HttpClient's own time. A new client
            // each time, as the downloads are far apart
            using var httpClient = isFirstRun
                ? ProxyListHttpClient.Create(firstRunSocketFactory, FirstListDownloadTimeout)
                : ProxyListHttpClient.Create(socketFactory: null, timeout: null);

            var newEndPoints = await ProxyEndPointUpdater.LoadFromUrlAsync(httpClient, url, cancellationToken).Vhc();
            if (newEndPoints.Length == 0) {
                VhLogger.Instance.LogWarning("No proxies found in downloaded content from {Url}", url);
                return;
            }

            // merge into the shared store and reload the working set with preserved statuses, with no flush between
            IReadOnlySet<(ProxyProtocol, string, int)> enabledBefore;
            bool isWaitedFor;
            using (await _flushLock.LockAsync(cancellationToken).Vhc()) {
                enabledBefore = GetEnabledAddresses();
                isWaitedFor = Volatile.Read(ref _endPointWaiterCount) > 0;
                await FlushCore(cancellationToken).Vhc();
                await _store.Merge(newEndPoints, _autoUpdateOptions.MaxItemCount, _autoUpdateOptions.MaxPenalty,
                    _autoUpdateOptions.RemoveDuplicateIps, cancellationToken).Vhc();

                await ReloadEntries(cancellationToken).Vhc();
                firstListUpdate.TrySetResult();
            }

            VhLogger.Instance.LogInformation("Downloaded and merged proxy list. Total proxies: {Count}",
                _proxyEndPointEntries.Length);

            if (isFirstRun)
                StartAddedCheck(enabledBefore, isWaitedFor);
            else
                StartListCheck();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // a restart or the close
        }
        catch (ObjectDisposedException) when (_disposed) {
            // the close, during the merge
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Failed to update proxy list from {Url}", url);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// While no endpoint is enabled, it waits for the first download of the list's update, and, where new options
    /// replace that update, for the new one's; a reload of the endpoints that brings an enabled one, as by
    /// <see cref="UpdateOptions"/>, ends the wait.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The connector was disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<bool> WaitForEndPoints(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        bool hasEnabledEndPoint;
        while (true) {
            // the reload's signal before the look at the endpoints: a reload after the look completes this one
            var entriesReload = Volatile.Read(ref _entriesReload);
            hasEnabledEndPoint = HasEnabledEndPoint;
            if (hasEnabledEndPoint)
                break;

            // the download to wait for, read under the lock a restart takes, which puts the new one in place as it
            // completes the old one
            TaskCompletionSource? firstListUpdate;
            lock (_autoUpdateLock)
                firstListUpdate = _firstListUpdate is { Task.IsCompleted: false } current ? current : null;

            // with none coming, the answer is the look, unless a reload came since it, as the download's own merge
            if (firstListUpdate == null) {
                if (entriesReload.Task.IsCompleted)
                    continue;

                break;
            }

            // counted while it waits, so a reload that brings endpoints leaves their check to it
            Interlocked.Increment(ref _endPointWaiterCount);
            try {
                await Task.WhenAny(firstListUpdate.Task, entriesReload.Task).WaitAsync(cancellationToken).Vhc();
            }
            finally {
                Interlocked.Decrement(ref _endPointWaiterCount);
            }
        }

        // a cancellation is the caller's, never an answer that no endpoint is there. The answer is the look that ended
        // the wait: a second look could find a reload's change while a download is still coming
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        return hasEnabledEndPoint;
    }

    // Connects through the proxy to the VPN server and, with VerifyTls, completes a TLS handshake with it, as the VPN's
    // own connection would: a proxy that opens the tunnel and passes nothing fails here, and one that stands in for the
    // server with a certificate of its own fails the server's check. VerifyTls as the check read it at its start
    private async Task<TimeSpan> CheckConnectionAsync(IProxyClient proxyClient, TcpClient tcpClient,
        CheckTarget target, bool verifyTls, CancellationToken cancellationToken)
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
                if (verifyTls) {
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
    }

    // A dial of the proxy alone, closed at once, within a check's time: a check that is to wait for its place at the
    // handshakes learns first whether the proxy answers at all, and waits with nothing open, which the proxy or the server
    // would close
    private async Task ProbeProxy(ISocketFactory socketFactory, IPEndPoint proxyEndPoint,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(_serverCheckTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        using var tcpClient = socketFactory.CreateTcpClient(proxyEndPoint);
        try {
            await tcpClient.ConnectAsync(proxyEndPoint, linkedCts.Token).Vhc();
        }
        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested) {
            throw new TimeoutException("Connection check timed out.", ex);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It checks the enabled endpoints, those that succeeded last first, the best rated first, then those never used,
    /// then those that failed last. Its task ends at the first that passes, or at the check's end where none does: a
    /// connect waits for it, and one slow proxy among many would hold the connect for its whole time. The check goes on
    /// behind it, as the one after a list's update does, then through those never rated that a reload brought
    /// meanwhile, as the list's first download does, so the connections that follow take every endpoint that works;
    /// on iOS and tvOS, once one passed, its TLS handshakes take their turns with those of the other checks nobody waits
    /// for, 3 at a time, and it dials as many at once as before. A cancellation before the first pass stops it, as do
    /// the
    /// next call and the close, which waits for its end; a call canceled already starts nothing, and stops no check.
    /// What it found is recorded as it comes and saved at its end, a stopped check's too; once the connector is closed,
    /// the close's own save takes it. The checks behind the session, as the update from a URL's, then take the factory
    /// and server of the last check through which a proxy reached the server.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The connector was disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task CheckServers(ISocketFactory socketFactory, IPEndPoint ipEndPoint, string? hostName,
        RemoteCertificateValidationCallback? serverCertificateValidation, CancellationToken cancellationToken)
    {
        // a call canceled already stops nothing: the last run goes on
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var target = new CheckTarget(ipEndPoint, hostName, serverCertificateValidation);
        var firstPass = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stop = new CancellationTokenSource();
        StartConnectCheck(socketFactory, target, GetEnabledEntriesForConnectCheck, firstPass, stop, replace: true);
        try {
            await firstPass.Task.WaitAsync(cancellationToken).Vhc();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // a connect canceled before a pass takes its check with it
            stop.TryCancel();
            throw;
        }

        // the close's stop ends the wait too, with no pass
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    // Puts a run of the check before a connect in place, with the entries it takes, its first pass and its stop, and
    // starts it: in place of the last one, which stops, or, without replace, only where none is under way, as it
    // returns. The close stops it, as it stops every check
    private bool StartConnectCheck(ISocketFactory socketFactory, CheckTarget target,
        Func<IReadOnlyList<ProxyEndPointEntry>> getEntries, TaskCompletionSource firstPass, CancellationTokenSource stop,
        bool replace)
    {
        CancellationTokenSource? oldStop;
        lock (_checkLock) {
            if (!replace && _connectCheckStop != null)
                return false;

            oldStop = _connectCheckStop;
            _connectCheckStop = stop;
        }

        oldStop?.TryCancel();
        _ = RunConnectCheck(socketFactory, target, getEntries, firstPass, stop);
        return true;
    }

    // the connections waiting for the list's endpoints; a test's to look at
    internal int EndPointWaiterCount => Volatile.Read(ref _endPointWaiterCount);

    // whether a run of the check before a connect is under way, which takes the endpoints a reload brings; a test's to
    // look at
    internal bool IsConnectCheckUnderWay {
        get {
            lock (_checkLock)
                return _connectCheckStop != null;
        }
    }

    // The endpoints a reload brought or turned on that no check rated, as a first download's or the app's, get a check
    // behind the session, where none is under way, which takes them, and a server is known; a reset that changed no
    // endpoint brings none. Where a connection waited for the list, it checks them itself, against its own server
    private void StartAddedCheck(IReadOnlySet<(ProxyProtocol, string, int)> enabledBefore, bool isWaitedFor)
    {
        if (!isWaitedFor && _lastCheck is { } lastCheck)
            StartConnectCheck(lastCheck.SocketFactory, lastCheck.Target,
                () => GetEnabledUnratedEntries().Where(x => !enabledBefore.Contains(GetAddress(x))).ToArray(),
                CreatePassed(), new CancellationTokenSource(), replace: false);
    }

    // A later download's check of the whole list, behind the session, in the place of the check before a connect: one
    // under way takes what the download brought, and the next check before a connect stops this one
    private void StartListCheck()
    {
        if (_lastCheck is { } lastCheck)
            StartConnectCheck(lastCheck.SocketFactory, lastCheck.Target, GetEnabledEntriesByQuality, CreatePassed(),
                new CancellationTokenSource(), replace: false);
    }

    // a first pass that came already: for a run nobody waits for, which goes beside the session from its start
    private static TaskCompletionSource CreatePassed()
    {
        var passed = new TaskCompletionSource();
        passed.SetResult();
        return passed;
    }

    // The check before a connect, which ends the connect's wait at its first pass, or at its end where none passes, and
    // goes on behind it through the rest as the check after a list's update does: the connections then take every
    // endpoint that works, where those its pass stopped stayed unrated, and the list's later downloads, which check it
    // again, may never come. A reload during the run, as by the list's first download, may bring endpoints it never
    // saw: it goes on over those never rated, until a run of it sees no reload. Once a pass came, its TLS handshakes
    // take their turns at HandshakePlaces, as it goes beside the session; a run nobody waits for, from its start. Nobody
    // waits for its end but the close, so it logs a failure
    private async Task RunConnectCheck(ISocketFactory socketFactory, CheckTarget target,
        Func<IReadOnlyList<ProxyEndPointEntry>> getEntries, TaskCompletionSource firstPass,
        CancellationTokenSource stop)
    {
        var cancellationToken = stop.Token;
        try {
            var entriesReload = Volatile.Read(ref _entriesReload);
            var entries = getEntries();
            while (true) {
                if (entries.Count > 0)
                    await CheckServers(socketFactory, target, entries, satisfiedSuccessCount: 10,
                        stopWhenSatisfied: false, HandshakePlaces, isBesideSession: firstPass.Task.IsCompleted,
                        onPass: () => firstPass.TrySetResult(), rated: null, cancellationToken).Vhc();

                // The last look at the reload and the run's end in one step under the lock: a reload after it finds
                // no run under way, and its download starts one of its own
                lock (_checkLock) {
                    if (!entriesReload.Task.IsCompleted) {
                        if (_connectCheckStop == stop)
                            _connectCheckStop = null;

                        break;
                    }
                }

                entriesReload = Volatile.Read(ref _entriesReload);
                entries = GetEnabledUnratedEntries();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // the next check before a connect, or the connect's own cancellation before a pass
        }
        catch (ObjectDisposedException) when (_disposed) {
            // the close
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not check the proxy endpoints.");
        }
        finally {
            firstPass.TrySetResult();
            lock (_checkLock) {
                if (_connectCheckStop == stop)
                    _connectCheckStop = null;
            }
        }
    }

    private IReadOnlyList<ProxyEndPointEntry> GetEnabledUnratedEntries() => _proxyEndPointEntries
        .Where(x => x.EndPoint.IsEnabled && x.Status.Quality is StatusQuality.Unknown)
        .ToArray();

    // never rated first, as the whole list's check is to rate them
    private IReadOnlyList<ProxyEndPointEntry> GetEnabledEntriesByQuality() => _proxyEndPointEntries
        .Where(x => x.EndPoint.IsEnabled)
        .OrderBy(x => x.Status.Quality)
        .ToArray();

    // those that succeeded last first, the best rated first, as the connect waits for the first to pass; then those
    // never used, then those that failed last. The quality's own order puts the never used first. Once 10 have passed,
    // the check skips every one already rated
    private IReadOnlyList<ProxyEndPointEntry> GetEnabledEntriesForConnectCheck() => _proxyEndPointEntries
        .Where(x => x.EndPoint.IsEnabled)
        .OrderBy(x => x.Status.IsLastUsedSucceeded ? 0 : x.Status.Quality is StatusQuality.Unknown ? 1 : 2)
        .ThenBy(x => x.Status.Quality)
        .ToArray();

    // With stopWhenSatisfied, the run ends at its successes: the checks under way end, and count for nothing. Each
    // outcome is recorded as it comes, the endpoint's address put in rated, and each pass then reported to onPass, as
    // a connect may go on at the first. It dials CheckWidth at a time. Beside the session, from its start or once a
    // proxy reached a server, a check with a TLS handshake ahead, an HTTPS proxy's own or the server's, takes its turn at
    // the handshake places given, beside those that started before. The close stops the run, which then throws
    // ObjectDisposedException
    private async Task CheckServers(ISocketFactory socketFactory,
        CheckTarget target,
        IEnumerable<ProxyEndPointEntry> endpoints,
        int satisfiedSuccessCount,
        bool stopWhenSatisfied,
        SemaphoreSlim? handshakePlaces,
        bool isBesideSession,
        Action? onPass,
        ISet<(ProxyProtocol, string, int)>? rated,
        CancellationToken cancellationToken)
    {
        // initialize progress tracker
        var progressMonitor = new ProgressMonitor(
            totalTaskCount: _proxyEndPointEntries.Length,
            taskTimeout: _serverCheckTimeout,
            maxDegreeOfParallelism: CheckWidth);

        // counted under the lock the close takes, which stops the checks under way and waits for them before its save
        lock (_checkLock) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _checkCount++;
        }

        var handshakeGate = new HandshakeGate(handshakePlaces, isBesideSession, () => _lastCheck != null);
        try {
            VhLogger.Instance.LogDebug("Checking proxy servers for reachability...");
            _progressMonitor = progressMonitor;
            using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closeCts.Token);
            var parallelOptions = new ParallelOptions {
                CancellationToken = runCts.Token,
                MaxDegreeOfParallelism = CheckWidth
            };

            // check all endpoints in parallel. If enough good servers found, stop checking more.
            var successCount = 0;
            TimeSpan? fastestLatency = null;
            try {
                await Parallel.ForEachAsync(endpoints, parallelOptions, async (entry, ct) => {
                    TcpClient? tcpClient = null;
                    SemaphoreSlim? handshakePlace = null;
                    TimeSpan? latency = null;
                    Exception? error = null;
                    var isCanceled = false;
                    var isChecking = false;
                    try {
                        // on the entry held now, as a dial's place may come long after the list was taken
                        if (TryGetCurrentEntry(entry) is not { } currentEntry || !IsToCheck(currentEntry))
                            return;

                        var proxyClient = await ProxyClientFactory.CreateProxyClient(currentEntry.EndPoint, ct).Vhc();
                        isChecking = true;

                        // Beside the session, a check with a TLS handshake ahead learns first whether the proxy answers,
                        // then waits for its place with nothing open, and goes on only where the entry is still to check,
                        // with the credentials it has then, at the address it dialed. Read once, as new options may change
                        // it meanwhile
                        var verifyTls = _verifyTls;
                        if (handshakeGate.IsLimited &&
                            (verifyTls || currentEntry.EndPoint.Protocol is ProxyProtocol.Https)) {
                            await ProbeProxy(socketFactory, proxyClient.ProxyEndPoint, ct).Vhc();
                            handshakePlace = await handshakeGate.Enter(ct).Vhc();
                            if (TryGetCurrentEntry(entry) is not { } heldEntry || !IsToCheck(heldEntry))
                                return;

                            proxyClient = ProxyClientFactory.CreateProxyClient(heldEntry.EndPoint,
                                proxyClient.ProxyEndPoint);
                        }

                        tcpClient = socketFactory.CreateTcpClient(proxyClient.ProxyEndPoint);
                        tcpClient.ReceiveBufferSize = 1024 * 4;
                        tcpClient.SendBufferSize = 1024 * 4;
                        latency = await CheckConnectionAsync(proxyClient, tcpClient, target, verifyTls, ct).Vhc();
                    }
                    catch (Exception ex) {
                        // whether the run was canceled when the check failed, before the socket's close below
                        error = ex;
                        isCanceled = ct.IsCancellationRequested;
                    }
                    finally {
                        // here, whatever ends the run: a canceled run left its sockets open
                        tcpClient?.Dispose();
                        handshakePlace?.Release();
                    }

                    // Recorded under the flush lock, as a reload of the entries runs under it: onto the entry held then,
                    // which no reload replaces until it is saved, and nowhere where the list lost it. Not under the run's
                    // token: what it found is kept. The run's fastest pass so far is the bar a slow one is penalized by.
                    // A pass of one turned off since releases no connect
                    var isPass = false;
                    using (await _flushLock.LockAsync(CancellationToken.None).Vhc()) {
                        if (TryGetCurrentEntry(entry) is { } currentEntry) {
                            if (latency < fastestLatency || fastestLatency is null)
                                fastestLatency = latency;

                            RecordCheckResult(currentEntry, latency, fastestLatency, error, isCanceled);
                            isPass = error is null && currentEntry.EndPoint.IsEnabled;
                            if (error is null || !isCanceled)
                                rated?.Add(GetAddress(currentEntry));
                        }
                    }

                    // once its outcome is taken: a check that ran, or a failure before it that the run's cancellation
                    // did not make
                    if (isChecking || !isCanceled)
                        progressMonitor.IncrementCompleted();

                    if (!isPass)
                        return;

                    // a server a proxy reached, which the checks behind the session take, and from which the checks
                    // take their turns at the handshakes
                    _lastCheck = new LastCheck(socketFactory, target);

                    onPass?.Invoke();
                    if (Interlocked.Increment(ref successCount) >= satisfiedSuccessCount && stopWhenSatisfied)
                        await runCts.CancelAsync().Vhc();

                    return;

                    // one turned off or gone since the list was taken is checked no more, nor, once 10 have passed, a
                    // rated one
                    bool IsToCheck(ProxyEndPointEntry heldEntry) =>
                        heldEntry.EndPoint.IsEnabled &&
                        (successCount < satisfiedSuccessCount || heldEntry.Status.Quality is StatusQuality.Unknown);
                }).Vhc();
            }
            catch (OperationCanceledException) when (runCts.IsCancellationRequested &&
                                                     !cancellationToken.IsCancellationRequested) {
                // the run ended at its successes, or the close stopped it
            }
            finally {
                // What it recorded, saved, a canceled run's too. After the close, whose own save takes this lock too, the
                // store is closed
                using (await _flushLock.LockAsync(CancellationToken.None).Vhc()) {
                    if (!_disposed)
                        await FlushCore(CancellationToken.None).Vhc();
                }
            }

            // make sure throw cancellation exception if cancelled
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
        finally {
            // its own: a check that started since shows its own progress
            Interlocked.CompareExchange(ref _progressMonitor, null, progressMonitor);
            lock (_checkLock) {
                if (--_checkCount == 0)
                    _checksEnded?.TrySetResult();
            }
        }
    }

    // An endpoint's check, onto the entry the connector holds now. A check that failed once its run was canceled is no
    // failure of its proxy, as the cancellation may have ended it
    private void RecordCheckResult(ProxyEndPointEntry entry, TimeSpan? latency, TimeSpan? fastestLatency,
        Exception? error, bool isCanceled)
    {
        if (error is null) {
            RecordSuccess(entry,
                latency: latency ?? throw new InvalidOperationException("A check that passed has no latency."),
                fastestLatency: fastestLatency, checkMode: true);

            return;
        }

        if (isCanceled)
            return;

        RecordFailed(entry, error, checkMode: true);

        // Disabled only where it is no proxy of its kind, an answer the server has no part in: a refusal, as SOCKS4's
        // "rejected or failed" is, may be for a server that is down, and a TLS failure may be the server's. An HTTP
        // proxy's refusal of the server, as a 403 for a port other than 443, comes as the same error as an answer that
        // is no HTTP at all, so an HTTP or HTTPS endpoint is never disabled here
        var isProtocolRejected = entry.EndPoint.Protocol is ProxyProtocol.Socks4 or ProxyProtocol.Socks5 &&
                                 error is ProxyClientException {
                                     SocketErrorCode: SocketError.ProtocolNotSupported
                                 };

        if (isProtocolRejected) {
            entry.EndPoint.IsEnabled = false;
            entry.IsDirty = true;
        }
    }

    // the enabled endpoints the connection may take, each in its turn: an endpoint's next turn moves back by 3 for each
    // point of its penalty
    private IReadOnlyList<ProxyEndPointEntry> GetOrderedEntriesQuery() => _proxyEndPointEntries
        .Where(x => x.EndPoint.IsEnabled)
        .Where(x => !UseRecentSucceeded || x.Status.LastSucceeded >= _sessionCreatedTime)
        .OrderBy(x => x.GetSortValue(_queuePosition))
        .ThenBy(x => x.Status.LastUsed)
        .ToArray();

    // Those that succeeded last first, each in its turn, then the rest in theirs. Each status read once: a check behind
    // the connect changes them meanwhile, and one read twice could fall in both groups, or in neither
    private static IReadOnlyList<ProxyEndPointEntry> SucceededFirst(IReadOnlyList<ProxyEndPointEntry> entries)
    {
        var isSucceeded = entries.Select(x => x.Status.IsLastUsedSucceeded).ToArray();
        return [
            .. entries.Where((_, i) => isSucceeded[i]),
            .. entries.Where((_, i) => !isSucceeded[i])
        ];
    }

    // A connection takes the endpoints that succeeded last first, and checks nothing while it has one. A check whenever
    // the first in turn had not succeeded ran at every connection: a pass ends a check, so the one at the head stayed
    // unrated, and at the head. Where it has to check, the rest of the list is checked behind its pass, as behind a
    // connect, where no such check is under way: after a change of network, say, every endpoint failed, and the
    // connections would stay on the one its check passed
    private async Task<IReadOnlyList<ProxyEndPointEntry>> GetOrderedEntries(ISocketFactory socketFactory,
        CheckTarget target,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _queuePosition);
        var endPointEntries = GetOrderedEntriesQuery();
        if (endPointEntries.Any(x => x.Status.IsLastUsedSucceeded))
            return SucceededFirst(endPointEntries);

        // The connections to this server wait for one check, which one of them may have run meanwhile; those to other
        // servers check beside it, as one that is down takes the check's whole time
        var checkLock = _checkLocks.GetOrAdd(target.EndPoint, _ => new AsyncLock());
        using var scopeLock = await checkLock.LockAsync(cancellationToken).Vhc();
        endPointEntries = GetOrderedEntriesQuery();
        if (endPointEntries.Any(x => x.Status.IsLastUsedSucceeded))
            return SucceededFirst(endPointEntries);

        // None it may take succeeded last: they are checked against this connection's server, until one succeeds, as a
        // check against another, which may be down, rated them all failed. Where only the endpoints that succeeded
        // lately may be taken and none has, all the enabled ones are checked
        var entriesToCheck = endPointEntries.Count > 0
            ? endPointEntries
            : _proxyEndPointEntries.Where(x => x.EndPoint.IsEnabled).ToArray();

        // It takes turns at the connections' own handshake places once a proxy reached a server, never behind the checks
        // nobody waits for: before, as for a key known by name alone, it runs within the server search's short probe,
        // which three handshakes that hang would outlast
        var rated = new HashSet<(ProxyProtocol, string, int)>();
        var isPassed = false;
        await CheckServers(socketFactory, target, entriesToCheck, satisfiedSuccessCount: 1, stopWhenSatisfied: true,
            ConnectionHandshakePlaces, isBesideSession: false, onPass: () => isPassed = true, rated,
            cancellationToken).Vhc();

        // The connection takes an endpoint that has just passed, before those the check ended unrated, and the run
        // behind it takes those this check left, where this check passed: a pass of a check of another server would
        // start it against this one, which none may reach. One it rated again would only stand beside the dial
        endPointEntries = SucceededFirst(GetOrderedEntriesQuery());
        if (isPassed)
            StartConnectCheck(socketFactory, target,
                () => GetEnabledEntriesForConnectCheck().Where(x => !rated.Contains(GetAddress(x))).ToArray(),
                CreatePassed(), new CancellationTokenSource(), replace: false);

        return endPointEntries;
    }

    // an endpoint known by its address, as a reload makes its objects anew
    private static (ProxyProtocol, string, int) GetAddress(ProxyEndPointEntry entry) =>
        (entry.EndPoint.Protocol, entry.EndPoint.Host, entry.EndPoint.Port);

    /// <inheritdoc/>
    /// <remarks>
    /// A connection takes the endpoints that succeeded last first, each in its turn; where none it may take has, it
    /// waits for the check of its server, until one passes, which takes the certificate
    /// <paramref name="serverCertificateValidation"/> takes, and the rest of the list, what that check left, is then
    /// checked behind it. Where every endpoint it took fails, it tries those that passed a check since; one turned off
    /// or dropped since it took them is not dialed. Each success and failure is recorded, and reported to
    /// <paramref name="onAttempt"/>; a failure taken for the caller's cancellation, any
    /// <see cref="OperationCanceledException"/> within the check timeout, is neither, and a cancellation of
    /// <paramref name="cancellationToken"/> during the last attempt is thrown. The close stops a dial under way, which
    /// records nothing and hands out no connection. With <see cref="ProxyAutoUpdateOptions.RemoveDuplicateIps"/>, a
    /// success disables the other enabled endpoints of its host and protocol, where its own is still on.
    /// While no endpoint is enabled, a connection first waits for the first download of the list's update from its URL,
    /// which starts with the connector, and fails where that brings none, as it does where there is no URL: it goes
    /// through a proxy or not at all.
    /// </remarks>
    /// <exception cref="SocketException">
    /// No endpoint could connect, or none is enabled, with <see cref="SocketError.NetworkUnreachable"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The connector was disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<ProxyConnection> ConnectAsync(ISocketFactory socketFactory, IPEndPoint ipEndPoint,
        string? hostName, RemoteCertificateValidationCallback? serverCertificateValidation, Action? onAttempt,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var target = new CheckTarget(ipEndPoint, hostName, serverCertificateValidation);

        // A list with no enabled endpoint waits for its first download, which starts with the connector: the connection
        // goes through a proxy or fails, never straight to the server. A caller that waited already (WaitForEndPoints)
        // goes on at once
        await WaitForEndPoints(cancellationToken).Vhc();

        // get ordered endpoints
        var entries = await GetOrderedEntries(socketFactory, target, cancellationToken).Vhc();

        // Those that pass behind the connect after the connection took its list come after it: one limited to the
        // endpoints that succeeded lately, as the server search's probes are, failed where those it took all did. Known
        // by their addresses, as a reload makes their objects anew
        var triedEndPoints = new HashSet<(ProxyProtocol, string, int)>();
        while (entries.Count > 0) {
            foreach (var entry in entries) {
                triedEndPoints.Add(GetAddress(entry));
                if (await TryConnect(entry, entries).Vhc() is { } connection)
                    return connection;
            }

            entries = GetOrderedEntriesQuery()
                .Where(x => x.Status.IsLastUsedSucceeded && !triedEndPoints.Contains(GetAddress(x)))
                .ToArray();
        }

        // could not connect to any proxy server; a cancellation that ended the last try is the caller's
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        throw new SocketException((int)SocketError.NetworkUnreachable);

        // a connection through the endpoint, or null where it failed, or where it was turned off or dropped since the
        // connection took its list
        async Task<ProxyConnection?> TryConnect(ProxyEndPointEntry listEntry,
            IReadOnlyList<ProxyEndPointEntry> listEntries)
        {
            var tickCount = Environment.TickCount64;
            TcpClient? tcpClient = null;
            ProxyConnection? connection = null;
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (TryGetCurrentEntry(listEntry) is not { EndPoint.IsEnabled: true } entry)
                return null;

            // the close stops the dial: a connection made after its save is closed rather than handed out
            using var dialCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closeCts.Token);
            try {
                VhLogger.Instance.LogDebug(
                    "Connecting via a {ProxyType} proxy server {ProxyServer}...",
                    entry.EndPoint.Protocol, VhLogger.FormatHostName(entry.EndPoint.Host));

                // create proxy client
                var proxyClient = await ProxyClientFactory.CreateProxyClient(entry.EndPoint, dialCts.Token).Vhc();
                tcpClient = socketFactory.CreateTcpClient(proxyClient.ProxyEndPoint);

                // connect to the target endpoint; the connection at once, which an HTTPS proxy's TLS session is part of
                await proxyClient.ConnectAsync(tcpClient, ipEndPoint, dialCts.Token).Vhc();
                connection = ProxyClientFactory.GetConnection(proxyClient, tcpClient);
                ObjectDisposedException.ThrowIf(_disposed, this);

                // find the fastest server
                var latency = TimeSpan.FromMilliseconds(Environment.TickCount64 - tickCount);
                if (!listEntries.Contains(_fastestEntry) ||
                    latency < _fastestEntry?.Status.Latency)
                    _fastestEntry = listEntry;

                // Onto the entry the connector holds now, which a reload during the connect made anew, and with that
                // entry's reset count: a reset after it drops the success, so the failure that may come after takes
                // nothing back
                var currentEntry = GetCurrentEntry(entry);
                RecordSuccess(currentEntry, latency: latency, fastestLatency: _fastestEntry?.Status.Latency,
                    checkMode: false);

                // a server a proxy reached, for the checks behind the session, where no check gave one; this connection's
                // failure takes it back, as the VPN's TLS comes after it
                Interlocked.CompareExchange(ref _lastCheck, new LastCheck(socketFactory, target, tcpClient), null);

                // timed no later than the attempt callback, which may restart the caller's timeout
                _handedOutConnections.AddOrUpdate(tcpClient,
                    new HandedOutConnection(currentEntry, Environment.TickCount64, currentEntry.StatusResetCount));

                onAttempt?.Invoke();
                if (_autoUpdateOptions.RemoveDuplicateIps)
                    DisableDuplicates(entry);

                // the close came meanwhile: the connection is closed rather than handed out
                ObjectDisposedException.ThrowIf(_disposed, this);
                return connection;
            }
            catch (Exception) when (_disposed) {
                // the close came during the dial or before the handout: no connection handed out, and an HTTPS proxy's
                // TLS session closed with its socket
                if (connection != null)
                    connection.Dispose();
                else
                    tcpClient?.Dispose();

                throw new ObjectDisposedException(GetType().FullName);
            }
            catch (Exception ex) {
                var delay = TimeSpan.FromMilliseconds(Environment.TickCount64 - tickCount);
                VhLogger.Instance.LogError(ex,
                    "Failed to connect to {ProxyType} proxy server {ProxyServer}. Delay: {delay}",
                    entry.EndPoint.Protocol, VhLogger.FormatHostName(entry.EndPoint.Host), delay);

                if (!IsCallerCancellation(ex, delay)) {
                    RecordFailed(GetCurrentEntry(entry), ex, checkMode: false);
                    onAttempt?.Invoke();
                }

                if (connection != null)
                    connection.Dispose();
                else
                    tcpClient?.Dispose();

                return null;
            }
        }
    }

    // Turns off the other enabled endpoints of the host and protocol of one whose connection succeeded, those the
    // connection did not take too, where it is still on itself, in one step: two connections through duplicates at once
    // would otherwise turn off each other, and the host with them. One read of the list: the winner and its duplicates
    // from the same one
    private void DisableDuplicates(ProxyEndPointEntry entry)
    {
        lock (_duplicateLock) {
            var entries = _proxyEndPointEntries;
            if (FindCurrentEntry(entries, entry) is not { EndPoint.IsEnabled: true } winner)
                return;

            var url = winner.EndPoint.BuildUrlWithoutPassword();
            var duplicates = entries.Where(x =>
                x.EndPoint.IsEnabled &&
                x.EndPoint.Protocol == winner.EndPoint.Protocol &&
                x.EndPoint.Host.Equals(winner.EndPoint.Host, StringComparison.OrdinalIgnoreCase) &&
                x.EndPoint.Id != winner.EndPoint.Id);

            foreach (var duplicate in duplicates) {
                duplicate.EndPoint.IsEnabled = false;
                duplicate.Status.ErrorMessage = $"Duplicate IP disabled in favour of {url}";
                duplicate.IsDirty = true;
            }
        }
    }

    // The entry the connector holds now for one taken before, or null where the list lost it: a reload since made its
    // object anew, and a change to the old one is saved no more
    private ProxyEndPointEntry? TryGetCurrentEntry(ProxyEndPointEntry entry) =>
        FindCurrentEntry(_proxyEndPointEntries, entry);

    // found by its address's parts, which its Id hashes
    private static ProxyEndPointEntry? FindCurrentEntry(ProxyEndPointEntry[] entries, ProxyEndPointEntry entry)
    {
        var endPoint = entry.EndPoint;
        return Array.Find(entries, x => ReferenceEquals(x, entry)) ??
               Array.Find(entries, x =>
                   x.EndPoint.Protocol == endPoint.Protocol &&
                   x.EndPoint.Port == endPoint.Port &&
                   x.EndPoint.Host == endPoint.Host);
    }

    // the old one where the list lost it
    private ProxyEndPointEntry GetCurrentEntry(ProxyEndPointEntry entry) => TryGetCurrentEntry(entry) ?? entry;

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
    /// It counts once per connection: the endpoint's success is taken back and a failure recorded; after a reset of the
    /// statuses by <see cref="ProxyOptions.ResetStates"/>, which dropped that success, the failure alone. A connection it
    /// did not hand out changes nothing; an <see cref="OperationCanceledException"/> within the check timeout after the
    /// connection was made records nothing, and leaves the connection for a failure that comes after it. Where no check
    /// had passed and this connection gave the checks behind the session their server, they take it no more, after a
    /// cancellation too.
    /// </remarks>
    public void RecordFailed(TcpClient tcpClient, Exception ex)
    {
        if (!_handedOutConnections.TryGetValue(tcpClient, out var handedOut))
            return;

        // A server only this connection reached is no longer the checks': its VPN TLS failed, or did not end in time,
        // which a cancellation may be. Another connection's success gives one again
        if (_lastCheck is { } lastCheck && lastCheck.Connection == tcpClient)
            Interlocked.CompareExchange(ref _lastCheck, null, lastCheck);

        // a cancellation is no failure, and leaves the connection known for one that comes after it
        var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - handedOut.TickCount);
        if (IsCallerCancellation(ex, elapsed))
            return;

        // counted once, against the proxy that made the connection
        if (!_handedOutConnections.Remove(tcpClient))
            return;

        // its success is taken back, as its connection failed, but where a reset of the statuses since dropped it, and a
        // later connection's would go in its place
        var entry = GetCurrentEntry(handedOut.Entry);
        if (handedOut.StatusResetCount == Interlocked.Read(ref _statusResetCount))
            entry.TakeBackSuccess();

        RecordFailed(entry, ex, checkMode: false);
    }

    /// <summary>
    /// Stops the update, every check and the saving, waits for a run of the update and for the checks to end, saves the
    /// statuses not saved yet, logging a failure, and disposes the store.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask DisposeAsync()
    {
        // a second close waits for the first one's end, as its flush may still be under way
        using var scopeLock = await _disposeLock.LockAsync(CancellationToken.None).Vhc();
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        // under its lock, so new options cannot put a job in place after it; out of the field first, so its first run's
        // end does not start it
        lock (_autoUpdateLock) {
            var autoUpdateJob = _autoUpdateJob;
            _autoUpdateJob = null;
            autoUpdateJob?.Dispose();
            _firstListUpdate?.TrySetResult(); // its waiters go on, to find the connector closed
        }

        // Every check under way stops at once, and none starts after the count under its lock: the client's quick close
        // goes on, without waiting for this one, to close the VPN adapter, whose sockets the checks take, and a check
        // that failed with them would count against its proxy
        TaskCompletionSource? checksEnded = null;
        lock (_checkLock) {
            if (_checkCount > 0)
                checksEnded = _checksEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _closeCts.TryCancel();
        _flushJob.Dispose();

        // the runs under way, stopped above, end before the save below, which takes what their checks found
        using var listUpdateScope = await _listUpdateLock.LockAsync(CancellationToken.None).Vhc();
        if (checksEnded != null)
            await checksEnded.Task.Vhc();

        // persist pending statuses to the shared store; a failed flush must not keep the store open
        try {
            using (await _flushLock.LockAsync(CancellationToken.None).Vhc())
                await FlushCore(CancellationToken.None).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not save the proxy endpoint statuses.");
        }

        HandshakePlaces?.Dispose();
        ConnectionHandshakePlaces?.Dispose();
        _store.Dispose();
    }
}
