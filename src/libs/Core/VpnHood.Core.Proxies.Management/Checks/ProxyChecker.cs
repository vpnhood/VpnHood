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

// Runs of checks against a VPN server: through each proxy to the server and, with VerifyTls, a TLS handshake with it,
// which takes its turn at the places given. It records each outcome and keeps the last server a proxy reached
internal sealed class ProxyChecker(
    ProxyEndPointList list,
    ProxyConnectorClose close,
    TimeSpan serverCheckTimeout,
    bool verifyTls)
{
    private const int CheckServerMaxDegreeOfParallelism = 50;

    // The server the checks behind the session take, having no caller: the last one a check reached through a proxy,
    // not the last one asked, as a down server would rate every proxy failed. Where no check passed, as for a key known
    // by name alone, the first one a connection reached, which its failure takes back. One object, read whole
    private volatile LastCheck? _lastCheck;

    private ProgressMonitor? _progressMonitor;
    // the runs under way, counted under their lock, which the close stops and waits for before its save
    private readonly Lock _checkLock = new();
    private int _checkCount;
    private TaskCompletionSource? _checksEnded;

    // as new options set it, which each check reads once, at its start
    public bool VerifyTls { get; set; } = verifyTls;

    public ProgressStatus? Progress => _progressMonitor?.Progress;

    public LastCheck? LastCheck => _lastCheck;

    // how many endpoints a check dials at once, one beside the session too; a test's to change
    public int CheckWidth { get; set; } = CheckServerMaxDegreeOfParallelism;

    // TLS handshake places shared by the checks nobody waits for. 3 on iOS and tvOS, as their tunnel's own connects:
    // each handshake costs native memory against an extension's limit of about 50 MB. A check's first dial takes none,
    // so dead proxies hold none. Null elsewhere. The places are settable for tests
    public SemaphoreSlim? HandshakePlaces { get; set; } = CreateHandshakePlaces();

    // The connections' own checks' places beside the session, apart from the checks nobody waits for, which may hold
    // theirs a check's whole time while a connection waits. 3 on iOS and tvOS, null elsewhere
    public SemaphoreSlim? ConnectionHandshakePlaces { get; set; } = CreateHandshakePlaces();

    // The connections' own checks' places before any proxy reached a server, on every platform: the server search runs
    // up to 8 of them at once, 3 on iOS and tvOS (ConnectorService's gate), which made up to 400 handshakes, or 150
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
                checksEnded = _checksEnded =
                    new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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

    // Each outcome is recorded as it comes and each pass reported, as a connect may go on at the first; with
    // stopWhenSatisfied it ends at its successes, and the checks under way count for nothing. Handshakes take turns at
    // handshakePlaces beside the session, else at handshakePlacesBeforeServer. The close stops it, which then throws
    // ObjectDisposedException
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
        var progressMonitor = new ProgressMonitor(
            totalTaskCount: list.Entries.Length,
            taskTimeout: serverCheckTimeout,
            maxDegreeOfParallelism: CheckWidth);

        // under the lock the close reads: a run either throws here or is counted, and the close waits for it
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

                        // A check that takes its turn dials first, so a dead proxy takes no place, and keeps that
                        // connection where a place is free; else it waits with nothing open, which the proxy or the
                        // server would close, and dials anew, its time from then. It goes on only for an entry still to
                        // check, with its credentials then, at the address dialed. VerifyTls is read once, as new
                        // options may change it
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

                    // Under the flush lock, as a reload takes it: onto the entry held then, which a reload saves before
                    // it replaces it, nowhere where the list lost it, and not under the run's token, as what it found
                    // is kept. The run's fastest pass is the bar a slow one is penalized by; a pass of one turned off
                    // since releases no connect
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

                    // the checks behind the session take this server, and checks take turns at handshakes from now
                    _lastCheck = new LastCheck(socketFactory, target);

                    onPass?.Invoke();
                    if (Interlocked.Increment(ref successCount) >= satisfiedSuccessCount && stopWhenSatisfied)
                        await runCts.CancelAsync().Vhc();

                    return;

                    // not one turned off or gone since the list was taken, nor a rated one once enough passed
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
                // What it recorded, saved, a canceled run's too. After the close, whose own save takes this lock too,
                // the store is closed
                using (await list.FlushLock.LockAsync(CancellationToken.None).Vhc()) {
                    if (!close.IsClosed)
                        await list.Flush(CancellationToken.None).Vhc();
                }
            }

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

        // Disabled only where it is no proxy of its kind: a refusal may be for a server that is down, and a TLS failure
        // may be the server's. An HTTP proxy's refusal, as a 403 for a port other than 443, comes as the same error as
        // an answer that is no HTTP at all, so HTTP and HTTPS endpoints are never disabled here
        var isProtocolRejected = entry.EndPoint.Protocol is ProxyProtocol.Socks4 or ProxyProtocol.Socks5 &&
                                 error is ProxyClientException {
                                     SocketErrorCode: SocketError.ProtocolNotSupported
                                 };

        if (isProtocolRejected) {
            entry.EndPoint.IsEnabled = false;
            entry.IsDirty = true;
        }
    }

    // Through the proxy to the server and, with VerifyTls, a TLS handshake with it, as the VPN's own connection makes:
    // a proxy that opens the tunnel and passes nothing fails, as does one whose certificate the caller's check refuses.
    // Its time and latency run from startTime, when its socket was made, one dialed already where a place was free
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

    // before a check's turn at the handshakes, within a check's time: a proxy that never answers takes no place
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
