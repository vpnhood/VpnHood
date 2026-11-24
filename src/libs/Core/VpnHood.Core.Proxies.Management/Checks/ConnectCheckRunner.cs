using System.Net;
using System.Net.Security;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.EndPoints;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Core.Proxies.Management.Checks;

// The check before a connect, which lets the connect go on at its first pass and goes on behind it, and the checks
// behind the session: one run in charge at a time, as the next check before a connect stops the last one, which may
// still be ending
internal sealed class ConnectCheckRunner(ProxyChecker checker, ProxyEndPointList list, ProxyConnectorClose close)
{
    // The run under way's stop, under its lock, which the next check before a connect gives, beside the close's. Not
    // disposed: it has no timer, and a stop may come at any time
    private readonly Lock _stopLock = new();
    private CancellationTokenSource? _connectCheckStop;

    // a run under way takes the endpoints a reload brings; a test's to look at
    public bool IsConnectCheckUnderWay {
        get {
            lock (_stopLock)
                return _connectCheckStop != null;
        }
    }

    // the connector's CheckServers, the check before a connect
    public async Task CheckServers(ISocketFactory socketFactory, IPEndPoint ipEndPoint, string? hostName,
        RemoteCertificateValidationCallback? serverCertificateValidation, CancellationToken cancellationToken)
    {
        // a call canceled already stops nothing: the last run goes on
        close.ThrowIfClosed();
        cancellationToken.ThrowIfCancellationRequested();
        var target = new CheckTarget(ipEndPoint, hostName, serverCertificateValidation);
        var firstPass = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stop = new CancellationTokenSource();
        StartConnectCheck(socketFactory, target, list.GetEnabledEntriesForConnectCheck, firstPass, stop, replace: true);
        try {
            await firstPass.Task.WaitAsync(cancellationToken).Vhc();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // a connect canceled before a pass takes its check with it
            stop.TryCancel();
            throw;
        }

        // the close's stop ends the wait too, with no pass
        close.ThrowIfClosed();
    }

    // Starts a run in place of the last one, which stops, or, without replace, only where none is under way, as it
    // returns. The close stops it, as every check
    public bool StartConnectCheck(ISocketFactory socketFactory, CheckTarget target,
        Func<IReadOnlyList<ProxyEndPointEntry>> getEntries, TaskCompletionSource firstPass,
        CancellationTokenSource stop, bool replace)
    {
        CancellationTokenSource? oldStop;
        lock (_stopLock) {
            if (!replace && _connectCheckStop != null)
                return false;

            oldStop = _connectCheckStop;
            _connectCheckStop = stop;
        }

        oldStop?.TryCancel();
        _ = RunConnectCheck(socketFactory, target, getEntries, firstPass, stop);
        return true;
    }

    // The endpoints a reload brought or turned on that no check rated, as a first download's or the app's, get a check
    // behind the session where a server is known and none is under way, which takes them; never those on before, as a
    // reset leaves every endpoint unrated. Where a connection waited for the list, it checks them itself
    public void StartAddedCheck(IReadOnlySet<(ProxyProtocol, string, int)> enabledBefore, bool isWaitedFor)
    {
        if (!isWaitedFor && checker.LastCheck is { } lastCheck)
            StartConnectCheck(lastCheck.SocketFactory, lastCheck.Target,
                () => list.GetEnabledUnratedEntries()
                    .Where(x => !enabledBefore.Contains(ProxyEndPointList.GetAddress(x)))
                    .ToArray(),
                CreatePassed(), new CancellationTokenSource(), replace: false);
    }

    // A later download's check of the whole list, behind the session: one under way takes what the download brought,
    // and the next check before a connect stops this one
    public void StartListCheck()
    {
        if (checker.LastCheck is { } lastCheck)
            StartConnectCheck(lastCheck.SocketFactory, lastCheck.Target, list.GetEnabledEntriesByQuality,
                CreatePassed(), new CancellationTokenSource(), replace: false);
    }

    // a first pass that came already: for a run nobody waits for, which goes beside the session from its start
    public static TaskCompletionSource CreatePassed()
    {
        var passed = new TaskCompletionSource();
        passed.SetResult();
        return passed;
    }

    // Ends the connect's wait at its first pass, then goes on through the rest, which that pass left unrated and the
    // list's later downloads may never check: the connections then take every endpoint that works. It goes on over what
    // a reload brought, until a run sees no reload. Handshakes take turns at HandshakePlaces once a pass came, from the
    // start in a run nobody waits for. Nobody but the close waits for its end, so it logs a failure
    private async Task RunConnectCheck(ISocketFactory socketFactory, CheckTarget target,
        Func<IReadOnlyList<ProxyEndPointEntry>> getEntries, TaskCompletionSource firstPass,
        CancellationTokenSource stop)
    {
        var cancellationToken = stop.Token;
        try {
            var entriesReload = list.EntriesReload;
            var entries = getEntries();
            while (true) {
                if (entries.Count > 0)
                    await checker.CheckServers(socketFactory, target, entries, satisfiedSuccessCount: 10,
                        stopWhenSatisfied: false, checker.HandshakePlaces, handshakePlacesBeforeServer: null,
                        isBesideSession: firstPass.Task.IsCompleted, onPass: () => firstPass.TrySetResult(),
                        rated: null, cancellationToken).Vhc();

                // The last look at the reload and the run's end in one step under the lock: a reload after it finds
                // no run under way, and its download starts one of its own
                lock (_stopLock) {
                    if (!entriesReload.IsCompleted) {
                        if (_connectCheckStop == stop)
                            _connectCheckStop = null;

                        break;
                    }
                }

                entriesReload = list.EntriesReload;
                entries = list.GetEnabledUnratedEntries();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // the next check before a connect, or the connect's own cancellation before a pass
        }
        catch (ObjectDisposedException) when (close.IsClosed) {
            // the close
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not check the proxy endpoints.");
        }
        finally {
            firstPass.TrySetResult();
            lock (_stopLock) {
                if (_connectCheckStop == stop)
                    _connectCheckStop = null;
            }
        }
    }
}
