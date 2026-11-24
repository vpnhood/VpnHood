using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Core.Proxies.Management.Checks;
using VpnHood.Core.Proxies.Management.EndPoints;
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
/// A connection tries the enabled endpoints that succeeded last first, then the rest, each group in turn: an
/// endpoint's next turn moves back by 3 for each point of its penalty. Where all those fail, it tries the ones that
/// passed a check since it took them, and skips one turned off or dropped since. Where none it may take succeeded last,
/// the endpoints are first checked against the connection's own server, until one passes; the connections to one
/// server wait for its one check, and those to other servers check beside it, as a down server takes a check's whole
/// time. While no endpoint is enabled, a connection waits for the first download of the list's update, and fails where
/// it brings none: a connection goes through a proxy or not at all.
/// </para>
/// <para>
/// A check connects through each enabled endpoint, up to 50 at a time, to the VPN server the caller gives, and with
/// <see cref="ProxyOptions.VerifyTls"/> makes a TLS handshake with it, under the caller's check of its certificate, the
/// VPN connection's own, with none downloaded for its chain. The device reaches no one else through the proxies. Each
/// outcome is recorded as it comes; a check that a pass elsewhere stopped records nothing, and one of an endpoint
/// turned off or gone meanwhile is no pass.
/// </para>
/// <para>
/// The check before a connect
/// (<see cref="CheckServers(ISocketFactory, IPEndPoint, string?, RemoteCertificateValidationCallback?, CancellationToken)"/>)
/// lets the connect go on at the first endpoint that passes, those that succeeded last first, and goes on behind it:
/// through every enabled endpoint, only those never rated once 10 passed, then those never rated that a reload brought
/// meanwhile. The next check before a connect stops it. A connection's own check ends at its first pass, and what it
/// left is then checked behind it the same way, where no such check is under way. So are the endpoints the list's first
/// download brings, or the app adds or turns on, where a proxy reached a server, unless a connection waits for the
/// list. A later download checks the whole list where no check is under way and a proxy reached a server; the next
/// check before a connect stops that one too.
/// </para>
/// <para>
/// On iOS and tvOS the checks beside the session make TLS handshakes 3 at a time, an HTTPS proxy's own among them:
/// every check that comes once a proxy reached a server, and those nobody waits for from their start. The connections'
/// own checks have 3 places of their own, so a connection never waits behind a check nobody waits for. Before any proxy
/// reached a server, on every platform, the connections' own checks share 50 places: the server search probes up to 10
/// servers at once, 8 of whose connections are made at once, 3 on iOS and tvOS, each with a check of its own. A check
/// still waiting when a proxy reaches a server takes its turn where those that come then take theirs. A check that
/// takes turns dials as many at once as one that takes none: it dials its proxy first, so a proxy that never answers
/// takes no place, and goes on through that connection where a place is free; otherwise it closes it, waits with
/// nothing open, which the proxy or the server would close, and dials again.
/// </para>
/// <para>
/// A SOCKS endpoint whose check fails with a <see cref="VpnHood.Net.Proxies.ProxyClientException"/> of
/// <see cref="System.Net.Sockets.SocketError.ProtocolNotSupported"/>, as one that is no proxy of its kind, is disabled;
/// any other failure, which may be the server's, is recorded alone, as is that one of an HTTP or HTTPS endpoint, which
/// also stands for its refusal of the server, as of a port other than 443. The statuses are saved to the store every
/// 10 seconds, after a check, and at the close, which stops every check and a connection's dial, and waits for the
/// checks before its save.
/// </para>
/// </remarks>
public class ManagedProxyConnector : IProxyConnector
{
    private ProxyEndPointEntry? _fastestEntry;
    private readonly TimeSpan _serverCheckTimeout;
    private readonly IProxyEndPointStore _store;
    // one check of a server at a time, which the connections to it wait for
    private readonly ConcurrentDictionary<IPEndPoint, AsyncLock> _checkLocks = new();
    private readonly Job _flushJob;
    private readonly AsyncLock _disposeLock = new();
    private readonly ProxySessionStatus _sessionStatus = new();
    // the proxy that made each connection handed out, for a failure after its connect, as on TLS; weak, as most
    // connections never fail
    private readonly ConditionalWeakTable<TcpClient, HandedOutConnection> _handedOutConnections = new();
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(10);
    private readonly ProxyConnectorClose _close = new();
    private readonly ProxyEndPointList _list;
    private readonly ProxyChecker _checker;
    private readonly ConnectCheckRunner _checkRunner;
    private readonly ProxyAutoUpdater _autoUpdater;

    /// <inheritdoc/>
    /// <value><see langword="true"/> while the store has any endpoint, as last loaded.</value>
    public bool IsEnabled => _list.IsEnabled;

    /// <inheritdoc/>
    public bool UseRecentSucceeded { get; set; }

    /// <inheritdoc/>
    public ProgressStatus? Progress => _checker.Progress;

    // the checker's, a test's to change: see there
    internal int CheckWidth {
        get => _checker.CheckWidth;
        set => _checker.CheckWidth = value;
    }

    internal SemaphoreSlim? HandshakePlaces {
        get => _checker.HandshakePlaces;
        set => _checker.HandshakePlaces = value;
    }

    internal SemaphoreSlim? ConnectionHandshakePlaces {
        get => _checker.ConnectionHandshakePlaces;
        set => _checker.ConnectionHandshakePlaces = value;
    }

    internal SemaphoreSlim SearchHandshakePlaces {
        get => _checker.SearchHandshakePlaces;
        set => _checker.SearchHandshakePlaces = value;
    }

    // a test's to look at
    internal int EndPointWaiterCount => _autoUpdater.EndPointWaiterCount;

    internal bool IsConnectCheckUnderWay => _checkRunner.IsConnectCheckUnderWay;

    private ManagedProxyConnector(
        ProxyOptions proxyOptions,
        IProxyEndPointStore store,
        TimeSpan? serverCheckTimeout)
    {
        _serverCheckTimeout = serverCheckTimeout ?? TimeSpan.FromSeconds(7);
        _store = store;
        _list = new ProxyEndPointList(store);
        _checker = new ProxyChecker(_list, _close, _serverCheckTimeout, proxyOptions.VerifyTls);
        _checkRunner = new ConnectCheckRunner(_checker, _list, _close);
        _autoUpdater = new ProxyAutoUpdater(_list, store, _checkRunner, _close, proxyOptions.AutoUpdateOptions);

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
    /// network's resolvers over them, as under a kill switch that lets only the VPN's own traffic out. Or
    /// <see langword="null"/> for the system's sockets and resolver. The update's later downloads, and those of an
    /// update that <see cref="UpdateOptions"/> starts, take the system's in any case, which the platform routes.
    /// </param>
    /// <param name="serverCheckTimeout">
    /// How long a check of an endpoint may take, or <see langword="null"/> for 7 seconds.
    /// </param>
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
        await connector._list.Reload(cancellationToken).Vhc();
        connector._autoUpdater.Restart(socketFactory);

        return connector;
    }

    /// <inheritdoc/>
    public ProxyConnectorStatus Status {
        get {
            lock (_sessionStatus)
                return new ProxyConnectorStatus {
                    AutoUpdate = _autoUpdater.Options.Interval > TimeSpan.Zero && _autoUpdater.Options.Url != null,
                    SessionStatus = _sessionStatus,
                    IsAnySucceeded = _list.Entries.Any(x => x.EndPoint.IsEnabled && x.IsLastUsedAnswered),
                    SucceededServerCount =
                        _list.Entries.Count(x => x.EndPoint.IsEnabled && x.Status.IsLastUsedSucceeded),
                    FailedServerCount = _list.Entries.Count(x =>
                        x.EndPoint.IsEnabled && x.Status is { HasUsed: true, IsLastUsedSucceeded: false }),
                    UnknownServerCount =
                        _list.Entries.Count(x => x.EndPoint.IsEnabled && x.Status is { HasUsed: false }),
                    DisabledServerCount = _list.Entries.Count(x => !x.EndPoint.IsEnabled)
                };
        }
    }

    /// <summary>Gets the endpoints the connector holds, with their statuses.</summary>
    /// <returns>The connector's own endpoint and status objects, which later connections update.</returns>
    public IReadOnlyList<ProxyEndPointInfo> GetEndPointInfos() => _list.GetEndPointInfos();

    /// <inheritdoc/>
    /// <remarks>
    /// It saves the statuses not saved yet, or, with <see cref="ProxyOptions.ResetStates"/>, clears the stored counts
    /// and saves the enabled states not saved yet alone, which the reset keeps: the app resets the store before it
    /// asks, and no count held is written back over it. A failed reset, which it logs, changes nothing, and the
    /// statuses are saved as without one. It then loads the endpoints from the store again; where that fails after a
    /// reset, it clears the counts it holds too. Endpoints the reload brought or turned on that no check rated, as the
    /// app's, are checked behind the session against a server a proxy reached, unless a connection waits for the list,
    /// which checks them itself. The update from a URL starts again, with a download at once, when its URL or interval
    /// changed, even during the list's first download. Otherwise it starts again where the reload takes the last
    /// enabled endpoint, or leaves none with nothing to download the list, unless the list's first download is still
    /// coming. A connection waiting for the list goes on where the reload brings an enabled endpoint. The downloads of
    /// the new update take the system's sockets, which the platform routes, through the tunnel where it is up.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The connector was disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task UpdateOptions(ProxyOptions proxyOptions, CancellationToken cancellationToken)
    {
        _close.ThrowIfClosed();
        _checker.VerifyTls = proxyOptions.VerifyTls;

        // no flush between the save and the reload, whose entries would not have its changes. The close's own save
        // takes this lock, and closes the store after it
        var hadEnabledEndPoint = _list.HasEnabledEndPoint;
        IReadOnlySet<(ProxyProtocol, string, int)> enabledBefore;
        bool isWaitedFor;
        using (await _list.FlushLock.LockAsync(cancellationToken).Vhc()) {
            _close.ThrowIfClosed();
            enabledBefore = _list.GetEnabledAddresses();
            isWaitedFor = _autoUpdater.IsWaitedFor;

            // The app resets the store before it asks, so no held count goes back over the reset, only the enabled
            // states, which it keeps. A failed reset changes nothing, and the statuses are saved as without one
            var isReset = proxyOptions.ResetStates &&
                          await VhInvoker.TryInvokeAsync("reset the proxy endpoints' statuses", LogLevel.Warning,
                              ResetStatuses, defaultValue: false).Vhc();

            // Counted before the reload, whose entries carry the count, so a success taken before is taken back no
            // more; one during the reload's read lands on an entry it drops. Where the save or the read fails before
            // the swap, the held entries are cleared as the store's are, not written back over the reset
            if (isReset)
                _list.CountStatusReset();
            else
                await _list.Flush(cancellationToken).Vhc();

            var heldEntries = _list.Entries;
            try {
                if (isReset)
                    await _list.SaveEnabledStates(cancellationToken).Vhc();

                await _list.Reload(cancellationToken).Vhc();
            }
            catch when (isReset) {
                if (_list.Entries == heldEntries)
                    _list.ResetHeldStatuses();

                throw;
            }
        }

        // endpoints the app added or turned on, which no check took
        _checkRunner.StartAddedCheck(enabledBefore, isWaitedFor);

        _autoUpdater.ApplyOptions(proxyOptions.AutoUpdateOptions, hadEnabledEndPoint);

        async Task<bool> ResetStatuses()
        {
            await _store.ResetStatuses(cancellationToken).Vhc();
            return true;
        }
    }

    private ValueTask FlushJob(CancellationToken cancellationToken)
    {
        return new ValueTask(Flush(cancellationToken));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It saves the changed statuses and enabled states, and the queue position, one flush at a time. A change made
    /// while it writes is saved by the next flush, and a failed write leaves its changes for the next one too. Once the
    /// connector is closed, it saves nothing: the close saved, and closed the store.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task Flush(CancellationToken cancellationToken)
    {
        // one at a time: an earlier flush's write that ended after a later one's put back the older statuses
        using var scopeLock = await _list.FlushLock.LockAsync(cancellationToken).Vhc();
        if (!_close.IsClosed)
            await _list.Flush(cancellationToken).Vhc();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// While no endpoint is enabled, it waits for the first download of the list's update, and, where new options
    /// replace that update, for the new one's; a reload of the endpoints that brings an enabled one, as by
    /// <see cref="UpdateOptions"/>, ends the wait.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The connector was disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public Task<bool> WaitForEndPoints(CancellationToken cancellationToken) =>
        _autoUpdater.WaitForEndPoints(cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    /// It checks the enabled endpoints, those that succeeded last first, the best rated first, then those never used,
    /// then those that failed last. Its task ends at the first that passes, or at the check's end where none does: a
    /// connect waits for it, and one slow proxy among many would hold the connect for its whole time. The check goes on
    /// behind it, as the one after a list's update does, then through those never rated that a reload brought
    /// meanwhile, as the list's first download does, so the connections that follow take every endpoint that works; on
    /// iOS and tvOS, once one passed, its TLS handshakes take their turns with those of the other checks nobody waits
    /// for, 3 at a time, and it dials as many at once as before. A cancellation before the first pass stops it, as do
    /// the next call and the close, which waits for its end; a call canceled already starts nothing, and stops no
    /// check. What it found is recorded as it comes and saved at its end, a stopped check's too; once the connector is
    /// closed, the close's own save takes it. The checks behind the session, as the update from a URL's, then take the
    /// factory and server of the last check through which a proxy reached the server.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The connector was disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public Task CheckServers(ISocketFactory socketFactory, IPEndPoint ipEndPoint, string? hostName,
        RemoteCertificateValidationCallback? serverCertificateValidation, CancellationToken cancellationToken) =>
        _checkRunner.CheckServers(socketFactory, ipEndPoint, hostName, serverCertificateValidation, cancellationToken);

    // The endpoints that succeeded last first, with no check while one has: checked whenever the first in turn had not
    // succeeded, every connection would check, as a pass ends a check and leaves the head unrated. Where it checks, the
    // rest is checked behind its pass: after a change of network every endpoint failed, and the connections would stay
    // on the one its check passed
    private async Task<IReadOnlyList<ProxyEndPointEntry>> GetOrderedEntries(ISocketFactory socketFactory,
        CheckTarget target,
        CancellationToken cancellationToken)
    {
        _list.AdvanceQueue();
        var endPointEntries = _list.GetOrderedEntriesQuery(UseRecentSucceeded);
        if (endPointEntries.Any(x => x.Status.IsLastUsedSucceeded))
            return ProxyEndPointList.SucceededFirst(endPointEntries);

        // The connections to this server wait for one check, which one of them may have run meanwhile; those to other
        // servers check beside it, as one that is down takes the check's whole time
        var checkLock = _checkLocks.GetOrAdd(target.EndPoint, _ => new AsyncLock());
        using var scopeLock = await checkLock.LockAsync(cancellationToken).Vhc();
        endPointEntries = _list.GetOrderedEntriesQuery(UseRecentSucceeded);
        if (endPointEntries.Any(x => x.Status.IsLastUsedSucceeded))
            return ProxyEndPointList.SucceededFirst(endPointEntries);

        // against this connection's own server, as a check against another, which may be down, rated them all failed;
        // all the enabled ones where only recent successes may be taken and none is
        var entriesToCheck = endPointEntries.Count > 0
            ? endPointEntries
            : _list.Entries.Where(x => x.EndPoint.IsEnabled).ToArray();

        // Its handshakes take turns at the connections' own places once a proxy reached a server, never behind the
        // checks nobody waits for; before, within the server search's short probe, which 3 hanging handshakes would
        // outlast, at one check's width shared with the other servers' probes
        var rated = new HashSet<(ProxyProtocol, string, int)>();
        var isPassed = false;
        await _checker.CheckServers(socketFactory, target, entriesToCheck, satisfiedSuccessCount: 1,
            stopWhenSatisfied: true, _checker.ConnectionHandshakePlaces, _checker.SearchHandshakePlaces,
            isBesideSession: false, onPass: () => isPassed = true, rated, cancellationToken).Vhc();

        // The connection takes an endpoint that has just passed, before those the check ended unrated, and the run
        // behind it takes those this check left, where this check passed: a pass of a check of another server would
        // start it against this one, which none may reach. One it rated again would only stand beside the dial
        endPointEntries = ProxyEndPointList.SucceededFirst(_list.GetOrderedEntriesQuery(UseRecentSucceeded));
        if (isPassed)
            _checkRunner.StartConnectCheck(socketFactory, target,
                () => _list.GetEnabledEntriesForConnectCheck()
                    .Where(x => !rated.Contains(ProxyEndPointList.GetAddress(x)))
                    .ToArray(),
                ConnectCheckRunner.CreatePassed(), new CancellationTokenSource(), replace: false);

        return endPointEntries;
    }

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
        _close.ThrowIfClosed();
        var target = new CheckTarget(ipEndPoint, hostName, serverCertificateValidation);

        // a list with no enabled endpoint waits for its first download: the connection goes through a proxy or fails,
        // never straight to the server. A caller that waited already goes on at once
        await WaitForEndPoints(cancellationToken).Vhc();

        var entries = await GetOrderedEntries(socketFactory, target, cancellationToken).Vhc();

        // Those that pass behind the connect after the connection took its list come after it: one limited to the
        // endpoints that succeeded lately, as the server search's probes are, failed where those it took all did. Known
        // by their addresses, as a reload makes their objects anew
        var triedEndPoints = new HashSet<(ProxyProtocol, string, int)>();
        while (entries.Count > 0) {
            foreach (var entry in entries) {
                triedEndPoints.Add(ProxyEndPointList.GetAddress(entry));
                if (await TryConnect(entry, entries).Vhc() is { } connection)
                    return connection;
            }

            entries = _list.GetOrderedEntriesQuery(UseRecentSucceeded)
                .Where(x => x.Status.IsLastUsedSucceeded && !triedEndPoints.Contains(ProxyEndPointList.GetAddress(x)))
                .ToArray();
        }

        // could not connect to any proxy server; a cancellation that ended the last try is the caller's
        cancellationToken.ThrowIfCancellationRequested();
        _close.ThrowIfClosed();
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
            _close.ThrowIfClosed();
            if (_list.TryGetCurrentEntry(listEntry) is not { EndPoint.IsEnabled: true } entry)
                return null;

            // the close stops the dial: a connection made after its save is closed rather than handed out
            using var dialCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _close.Token);
            try {
                VhLogger.Instance.LogDebug(
                    "Connecting via a {ProxyType} proxy server {ProxyServer}...",
                    entry.EndPoint.Protocol, VhLogger.FormatHostName(entry.EndPoint.Host));

                var proxyClient = await ProxyClientFactory.CreateProxyClient(entry.EndPoint, dialCts.Token).Vhc();
                tcpClient = socketFactory.CreateTcpClient(proxyClient.ProxyEndPoint);

                // connect to the target endpoint; the connection at once, which an HTTPS proxy's TLS session is part of
                await proxyClient.ConnectAsync(tcpClient, ipEndPoint, dialCts.Token).Vhc();
                connection = ProxyClientFactory.GetConnection(proxyClient, tcpClient);
                _close.ThrowIfClosed();

                var latency = TimeSpan.FromMilliseconds(Environment.TickCount64 - tickCount);
                if (!listEntries.Contains(_fastestEntry) ||
                    latency < _fastestEntry?.Status.Latency)
                    _fastestEntry = listEntry;

                // Onto the entry the connector holds now, which a reload during the connect made anew, and with that
                // entry's reset count: a reset after it drops the success, so the failure that may come after takes
                // nothing back
                var currentEntry = _list.GetCurrentEntry(entry);
                RecordSuccess(currentEntry, latency: latency, fastestLatency: _fastestEntry?.Status.Latency);

                // a server a proxy reached, for the checks behind the session, where no check gave one; this
                // connection's failure takes it back, as the VPN's TLS comes after it
                _checker.OfferServerReached(new LastCheck(socketFactory, target, tcpClient));

                // timed no later than the attempt callback, which may restart the caller's timeout
                _handedOutConnections.AddOrUpdate(tcpClient,
                    new HandedOutConnection(currentEntry, Environment.TickCount64, currentEntry.StatusResetCount));

                onAttempt?.Invoke();
                if (_autoUpdater.Options.RemoveDuplicateIps)
                    _list.DisableDuplicates(entry);

                // the close came meanwhile: the connection is closed rather than handed out
                _close.ThrowIfClosed();
                return connection;
            }
            catch (Exception) when (_close.IsClosed) {
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
                    RecordFailed(_list.GetCurrentEntry(entry), ex);
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

    // onto the entry, and the session's count of connections
    private void RecordSuccess(ProxyEndPointEntry entry, TimeSpan latency, TimeSpan? fastestLatency)
    {
        _list.RecordSuccess(entry, latency, fastestLatency);
        lock (_sessionStatus) {
            _sessionStatus.SucceededCount++;
            _sessionStatus.LastSucceeded = DateTime.UtcNow; // once per connection: the coarse second made ties
            _sessionStatus.Latency = entry.Status.Latency;
            _sessionStatus.ErrorMessage = null;
        }
    }

    private void RecordFailed(ProxyEndPointEntry entry, Exception error)
    {
        _list.RecordFailed(entry, error);
        lock (_sessionStatus) {
            _sessionStatus.FailedCount++;
            _sessionStatus.LastFailed = DateTime.UtcNow;
            _sessionStatus.Latency = null;
            _sessionStatus.ErrorMessage = entry.Status.ErrorMessage;
        }
    }

    // let's not assume bad server if caller cancelled the operation: a cancellation within a server check's time, which
    // one with no end always is
    private bool IsCallerCancellation(Exception ex, TimeSpan elapsed) =>
        ex is OperationCanceledException &&
        (_serverCheckTimeout == Timeout.InfiniteTimeSpan || elapsed < _serverCheckTimeout);

    /// <inheritdoc/>
    /// <remarks>
    /// It counts once per connection: the endpoint's success is taken back and a failure recorded; after a reset of the
    /// statuses by <see cref="ProxyOptions.ResetStates"/>, which dropped that success, the failure alone. A connection
    /// it did not hand out changes nothing; an <see cref="OperationCanceledException"/> within the check timeout after
    /// the connection was made records nothing, and leaves the connection for a failure that comes after it. Where no
    /// check had passed and this connection gave the checks behind the session their server, they take it no more,
    /// after a cancellation too.
    /// </remarks>
    public void RecordFailed(TcpClient tcpClient, Exception ex)
    {
        if (!_handedOutConnections.TryGetValue(tcpClient, out var handedOut))
            return;

        // A server only this connection reached is no longer the checks': its VPN TLS failed, or did not end in time,
        // which a cancellation may be. Another connection's success gives one again
        _checker.TakeBackServerReached(tcpClient);

        // a cancellation is no failure, and leaves the connection known for one that comes after it
        var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - handedOut.TickCount);
        if (IsCallerCancellation(ex, elapsed))
            return;

        // counted once, against the proxy that made the connection
        if (!_handedOutConnections.Remove(tcpClient))
            return;

        // its success is taken back, as its connection failed, but where a reset of the statuses since dropped it, and
        // a later connection's would go in its place
        var entry = _list.GetCurrentEntry(handedOut.Entry);
        if (handedOut.StatusResetCount == _list.StatusResetCount)
            entry.TakeBackSuccess();

        RecordFailed(entry, ex);
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
        if (!_close.TryBegin())
            return;

        _autoUpdater.Stop();

        // Every check under way stops at once, and none starts after the count under its lock: the client's quick close
        // goes on, without waiting for this one, to close the VPN adapter, whose sockets the checks take, and a check
        // that failed with them would count against its proxy
        var checksEnded = _checker.CreateChecksEnded();
        _close.Stop();
        _flushJob.Dispose();

        // the runs under way, stopped above, end before the save below, which takes what their checks found
        using var listUpdateScope = await _autoUpdater.UpdateLock.LockAsync(CancellationToken.None).Vhc();
        if (checksEnded != null)
            await checksEnded.Vhc();

        // persist pending statuses to the shared store; a failed flush must not keep the store open
        try {
            using (await _list.FlushLock.LockAsync(CancellationToken.None).Vhc())
                await _list.Flush(CancellationToken.None).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not save the proxy endpoint statuses.");
        }

        _checker.DisposePlaces();
        _store.Dispose();
    }
}
