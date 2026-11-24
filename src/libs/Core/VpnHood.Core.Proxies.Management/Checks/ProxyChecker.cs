using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.EndPoints;
using VpnHood.Net.Proxies;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Monitoring;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Core.Proxies.Management.Checks;

// Runs of checks of the endpoints against a VPN server, CheckWidth at a time, each a connection through its proxy to the
// server and, with VerifyTls, a TLS handshake with it, which takes its turn at the places given. It records what each
// check found, keeps the server a proxy last reached, and counts the runs under way for the close
internal sealed class ProxyChecker(
    ProxyEndPointList list,
    ProxyConnectorClose close,
    TimeSpan serverCheckTimeout,
    bool verifyTls)
{
    private const int CheckServerMaxDegreeOfParallelism = 50;

    // The factory and VPN server of the last check through which a proxy reached the server, the VPN's TLS with
    // VerifyTls, which the checks behind the session take, as they have no caller of their own. Not the last call's: a
    // server search's probe of a server that is down would rate every proxy they check failed. Where no check passed,
    // the first connection's that reached its server, whose TLS comes after it: a key known by name alone may never
    // check in a session; that connection's failure takes it back. One object, so a reader takes one's whole
    private volatile LastCheck? _lastCheck;

    private ProgressMonitor? _progressMonitor;
    // The runs under way, counted under their lock, which the close stops at once and waits for before its save
    private readonly Lock _checkLock = new();
    private int _checkCount;
    private TaskCompletionSource? _checksEnded;

    // as new options set it, which each check reads once, at its start
    public bool VerifyTls { get; set; } = verifyTls;

    public ProgressStatus? Progress => _progressMonitor?.Progress;

    public LastCheck? LastCheck => _lastCheck;

    // how many endpoints a check dials at once, one beside the session too; a test's to change
    public int CheckWidth { get; set; } = CheckServerMaxDegreeOfParallelism;

    // The places for the TLS handshakes of the checks beside the session that nobody waits for, which they share: the
    // check behind a connect once a pass came, and the list's and the app's. On iOS and tvOS 3, as the tunnel's own
    // connects there (ConnectorService's gate): each handshake costs native memory outside every managed budget, against
    // an extension's limit of about 50 MB. A dial is no handshake, so a list whose proxies mostly never answer goes as
    // fast as elsewhere. Null elsewhere, where the dials' width is the only one. A test's to change; the close disposes it
    public SemaphoreSlim? HandshakePlaces { get; set; } = CreateHandshakePlaces();

    // The places for the TLS handshakes of the connections' own checks beside the session, which they share: apart from
    // those of the checks nobody waits for, which may hold theirs for a check's whole time while a connection waits. On
    // iOS and tvOS 3, so 6 at most beside the session. A test's to change; the close disposes it
    public SemaphoreSlim? ConnectionHandshakePlaces { get; set; } = CreateHandshakePlaces();

    // The places for the TLS handshakes of the connections' own checks before any proxy reached a server, which they
    // share on every platform: the server search probes up to 10 servers at once, 8 of whose connections are made at
    // once, 3 on iOS and tvOS (ConnectorService's gate), and each probe's check made as many handshakes as one check
    // alone, so up to 400, or 150. As wide as one check. A test's to change; the close disposes it
    public SemaphoreSlim SearchHandshakePlaces { get; set; } = new(CheckServerMaxDegreeOfParallelism);

    private static SemaphoreSlim? CreateHandshakePlaces() =>
        (OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst()) || OperatingSystem.IsTvOS()
            ? new SemaphoreSlim(3)
            : null;

    // a server a connection reached, for the checks behind the session, where no check gave one
    public void OfferServerReached(LastCheck connectionCheck) =>
        Interlocked.CompareExchange(ref _lastCheck, connectionCheck, null);

    // where that connection gave the checks their server
    public void TakeBackServerReached(TcpClient connection)
    {
        if (_lastCheck is { } lastCheck && lastCheck.Connection == connection)
            Interlocked.CompareExchange(ref _lastCheck, null, lastCheck);
    }

    // the end of the runs under way, where any is, which the close waits for: none starts after the close began
    public Task? CreateChecksEnded()
    {
        TaskCompletionSource? checksEnded = null;
        lock (_checkLock) {
            if (_checkCount > 0)
                checksEnded = _checksEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return checksEnded?.Task;
    }

    // at the close, once the runs ended
    public void DisposePlaces()
    {
        HandshakePlaces?.Dispose();
        ConnectionHandshakePlaces?.Dispose();
        SearchHandshakePlaces.Dispose();
    }

    // With stopWhenSatisfied, the run ends at its successes: the checks under way end, and count for nothing. Each
    // outcome is recorded as it comes, the endpoint's address put in rated, and each pass then reported to onPass, as a
    // connect may go on at the first. It dials CheckWidth at a time. A check with a TLS handshake ahead, an HTTPS
    // proxy's own or the server's, takes its turn at the handshake places given, beside those that started before:
    // those beside the session from the run's start or once a proxy reached a server, those before a server until then.
    // The close stops the run, which then throws ObjectDisposedException
    public async Task CheckServers(ISocketFactory socketFactory,
        CheckTarget target,
        IEnumerable<ProxyEndPointEntry> endpoints,
        int satisfiedSuccessCount,
        bool stopWhenSatisfied,
        SemaphoreSlim? handshakePlaces,
        SemaphoreSlim? handshakePlacesBeforeServer,
        bool isBesideSession,
        Action? onPass,
        ISet<(ProxyProtocol, string, int)>? rated,
        CancellationToken cancellationToken)
    {
        // initialize progress tracker
        var progressMonitor = new ProgressMonitor(
            totalTaskCount: list.Entries.Length,
            taskTimeout: serverCheckTimeout,
            maxDegreeOfParallelism: CheckWidth);

        // counted under the lock the close takes, which stops the checks under way and waits for them before its save
        lock (_checkLock) {
            close.ThrowIfClosed();
            _checkCount++;
        }

        var handshakeGate = new HandshakeGate(handshakePlaces, handshakePlacesBeforeServer, isBesideSession,
            () => _lastCheck != null);

        try {
            VhLogger.Instance.LogDebug("Checking proxy servers for reachability...");
            _progressMonitor = progressMonitor;
            using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, close.Token);
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
                        if (list.TryGetCurrentEntry(entry) is not { } currentEntry || !IsToCheck(currentEntry))
                            return;

                        var proxyClient = await ProxyClientFactory.CreateProxyClient(currentEntry.EndPoint, ct).Vhc();
                        isChecking = true;

                        // A check with a TLS handshake ahead that takes its turn dials its proxy first, so one that
                        // never answers takes no place, and goes on through that connection where a place is free.
                        // Otherwise it waits for one with nothing open, which the proxy or the server would close, and
                        // dials anew, its time from then. Either way it goes on only where the entry is still to check,
                        // with the credentials it has then, at the address it dialed. Read once, as new options may
                        // change it meanwhile
                        var verifyTls = VerifyTls;
                        tcpClient = CreateCheckClient(socketFactory, proxyClient.ProxyEndPoint);
                        var startTime = Environment.TickCount64;
                        if (handshakeGate.IsLimited && HasHandshakeAhead(currentEntry.EndPoint, target, verifyTls)) {
                            await DialProxy(tcpClient, proxyClient.ProxyEndPoint, ct).Vhc();
                            var hasWaited = !handshakeGate.TryEnter(out handshakePlace);
                            if (hasWaited) {
                                tcpClient.Dispose();
                                handshakePlace = await handshakeGate.Enter(ct).Vhc();
                            }

                            if (list.TryGetCurrentEntry(entry) is not { } heldEntry || !IsToCheck(heldEntry))
                                return;

                            proxyClient = ProxyClientFactory.CreateProxyClient(heldEntry.EndPoint,
                                proxyClient.ProxyEndPoint);

                            if (hasWaited) {
                                tcpClient = CreateCheckClient(socketFactory, proxyClient.ProxyEndPoint);
                                startTime = Environment.TickCount64;
                            }
                        }

                        latency = await CheckConnectionAsync(proxyClient, tcpClient, target, verifyTls, startTime,
                            ct).Vhc();
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
                    using (await list.FlushLock.LockAsync(CancellationToken.None).Vhc()) {
                        if (list.TryGetCurrentEntry(entry) is { } currentEntry) {
                            if (latency < fastestLatency || fastestLatency is null)
                                fastestLatency = latency;

                            RecordCheckResult(currentEntry, latency, fastestLatency, error, isCanceled);
                            isPass = error is null && currentEntry.EndPoint.IsEnabled;
                            if (error is null || !isCanceled)
                                rated?.Add(ProxyEndPointList.GetAddress(currentEntry));
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
                using (await list.FlushLock.LockAsync(CancellationToken.None).Vhc()) {
                    if (!close.IsClosed)
                        await list.Flush(CancellationToken.None).Vhc();
                }
            }

            // make sure throw cancellation exception if cancelled
            cancellationToken.ThrowIfCancellationRequested();
            close.ThrowIfClosed();
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
            list.RecordSuccess(entry,
                latency: latency ?? throw new InvalidOperationException("A check that passed has no latency."),
                fastestLatency: fastestLatency);

            return;
        }

        if (isCanceled)
            return;

        list.RecordFailed(entry, error);

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

    // Connects through the proxy to the VPN server and, with VerifyTls, completes a TLS handshake with it, as the VPN's
    // own connection would: a proxy that opens the tunnel and passes nothing fails here, and one that stands in for the
    // server with a certificate of its own fails the server's check. VerifyTls as the check read it at its start. Its
    // time and latency run from startTime, the start of its dial, which a socket dialed before it took its place began
    private async Task<TimeSpan> CheckConnectionAsync(IProxyClient proxyClient, TcpClient tcpClient,
        CheckTarget target, bool verifyTls, long startTime, CancellationToken cancellationToken)
    {
        using var serverCheckCts = new CancellationTokenSource(GetCheckTimeLeft(startTime));
        try {
            using var linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, serverCheckCts.Token);

            await proxyClient.ConnectAsync(tcpClient, target.EndPoint, linkedCts.Token).Vhc();
            var latency = TimeSpan.FromMilliseconds(Environment.TickCount64 - startTime);

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

    // A check's dial of its proxy alone, within a check's time, before it takes its turn at the handshakes: a proxy
    // that never answers takes no place
    private async Task DialProxy(TcpClient tcpClient, IPEndPoint proxyEndPoint, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(serverCheckTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try {
            await tcpClient.ConnectAsync(proxyEndPoint, linkedCts.Token).Vhc();
        }
        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested) {
            throw new TimeoutException("Connection check timed out.", ex);
        }
    }

    // what is left of a check's time that started at startTime: all of it where it has no end
    private TimeSpan GetCheckTimeLeft(long startTime)
    {
        if (serverCheckTimeout == Timeout.InfiniteTimeSpan)
            return Timeout.InfiniteTimeSpan;

        var timeLeft = serverCheckTimeout - TimeSpan.FromMilliseconds(Environment.TickCount64 - startTime);
        return timeLeft > TimeSpan.Zero ? timeLeft : TimeSpan.Zero;
    }

    // Small buffers: a check's socket carries handshakes alone, up to 50 at once. No delay, as the proxy clients set on
    // a socket they dial, which they leave as it is where it came dialed
    private static TcpClient CreateCheckClient(ISocketFactory socketFactory, IPEndPoint proxyEndPoint)
    {
        var tcpClient = socketFactory.CreateTcpClient(proxyEndPoint);
        try {
            tcpClient.NoDelay = true;
            tcpClient.ReceiveBufferSize = 1024 * 4;
            tcpClient.SendBufferSize = 1024 * 4;
            return tcpClient;
        }
        catch {
            tcpClient.Dispose();
            throw;
        }
    }

    // Whether a check through the endpoint comes to a TLS handshake: an HTTPS proxy's own, or the server's with
    // VerifyTls, which a SOCKS4 proxy never reaches for an IPv6 server, as its client refuses that before it dials
    private static bool HasHandshakeAhead(ProxyEndPoint endPoint, CheckTarget target, bool verifyTls) =>
        endPoint.Protocol is ProxyProtocol.Https ||
        verifyTls && !(endPoint.Protocol is ProxyProtocol.Socks4 &&
                       target.EndPoint.AddressFamily is AddressFamily.InterNetworkV6);
}
