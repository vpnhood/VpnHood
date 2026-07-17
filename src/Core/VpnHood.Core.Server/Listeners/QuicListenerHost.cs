using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Server.Access;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Connections;

namespace VpnHood.Core.Server.Listeners;

internal class QuicListenerHost(
    IQuicServer? quicServer,
    SessionManager sessionManager,
    CancellationToken cancellationToken,
    Func<IStreamConnection, CancellationToken, Task> processNewConnection)
    : IAsyncDisposable
{
    private readonly List<QuicListenerEntry> _listeners = [];
    private IReadOnlyList<CertificateHostName> _certificates = [];
    private bool _disposed;

    // only report listeners that are still running; a listener whose task has finished
    // (e.g. stopped due to an unrecoverable error) must not be advertised in the Hello response
    public IReadOnlyList<IPEndPoint> EndPoints =>
        _listeners.Where(x => !x.ListenerTask.IsCompleted).Select(x => x.Listener.LocalEndPoint).ToArray();

    public async Task<IReadOnlyList<ServerHostEndPointStatus>> Configure(
        IReadOnlyList<IPEndPoint> ipEndPoints,
        IReadOnlyList<CertificateHostName> certificates)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _certificates = certificates;

        // stop listeners that are no longer in the list
        foreach (var entry in _listeners
                     .Where(x => !ipEndPoints.Any(ep => ep.Equals(x.Listener.LocalEndPoint))).ToArray()) {
            VhLogger.Instance.LogInformation("Stop listening on QuicEndPoint: {QuicEndPoint}",
                VhLogger.Format(entry.Listener.LocalEndPoint));
            await entry.DisposeAsync().Vhc();
            _listeners.Remove(entry);
        }

        // start new listeners
        var endPointStatuses = new List<ServerHostEndPointStatus>();
        foreach (var ipEndPoint in ipEndPoints) {
            if (_listeners.Any(x => x.Listener.LocalEndPoint.Equals(ipEndPoint))) {
                endPointStatuses.Add(new ServerHostEndPointStatus { Protocol = ChannelProtocol.Quic, EndPoint = ipEndPoint });
                continue;
            }

            VhLogger.Instance.LogInformation("Start listening on QuicEndPoint: {QuicEndPoint}",
                VhLogger.Format(ipEndPoint));
            try {
                if (ipEndPoint.Port == 0)
                    throw new InvalidOperationException("QUIC port has not been specified.");

                if (_certificates.Count == 0)
                    throw new InvalidOperationException("No certificate has been configured for QUIC.");

                // QUIC provider is unavailable on this platform (e.g. msquic is not installed).
                // Throw here so it is recorded as a per-endpoint error below instead of failing the whole configuration.
                if (quicServer is null)
                    throw new NotSupportedException("QUIC is not supported on this platform.");

                var listenerOptions = new QuicListenerOptions {
                    ListenEndPoint = ipEndPoint,
                    IdleTimeout = sessionManager.SessionOptions.ChannelIdleTimeoutValue,
                    ServerCertificateSelector = SelectCertificate
                };
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var listener = await quicServer.ListenAsync(listenerOptions, cancellationToken).Vhc();
                var task = ListenTask(listener, cts.Token);
                _listeners.Add(new QuicListenerEntry(listener, task, cts));
                endPointStatuses.Add(new ServerHostEndPointStatus { Protocol = ChannelProtocol.Quic, EndPoint = ipEndPoint });
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Error listening on QuicEndPoint: {QuicEndPoint}",
                    VhLogger.Format(ipEndPoint));
                endPointStatuses.Add(new ServerHostEndPointStatus {
                    Protocol = ChannelProtocol.Quic,
                    EndPoint = ipEndPoint,
                    Error = ex.ToApiError()
                });
            }
        }

        // remove listeners whose task already finished (e.g. stopped due to unexpected error)
        _listeners.RemoveAll(x => x.ListenerTask.IsCompleted);
        return endPointStatuses;
    }

    private X509Certificate SelectCertificate(string? hostname)
    {
        var certificate =
            _certificates.SingleOrDefault(x => x.HostName.Equals(hostname, StringComparison.OrdinalIgnoreCase))
            ?? _certificates.First();

        return certificate.Certificate;
    }

    private async Task ListenTask(IQuicListener listener, CancellationToken ct)
    {
        var localEp = listener.LocalEndPoint;
        var errorCounter = 0;
        const int maxErrorCount = 200;

        while (!ct.IsCancellationRequested) {
            try {
                var quicConnection = await listener.AcceptConnectionAsync(ct).Vhc();
                _ = AcceptStreams(quicConnection, ct);

                // a successful accept proves the listener is healthy; reset the counter so that only
                // sustained (unrecoverable) failures trip the stop, not isolated per-connection errors
                errorCounter = 0;
            }
            catch (OperationCanceledException) {
                break;
            }
            catch (ObjectDisposedException) {
                break;
            }
            catch (Exception ex) {
                if (ct.IsCancellationRequested)
                    break;

                VhLogger.Instance.LogError(GeneralEventId.Request, ex,
                    "ServerHost could not AcceptQuicConnection. LocalEndPint: {LocalEndPint}, ErrorCounter: {ErrorCounter}",
                    localEp, errorCounter);

                // We cannot tell a per-connection fault from permanent damage (IP change, socket unbound)
                // by exception type: msquic collapses them into a generic QuicException/SocketException, and
                // .NET already drops failed handshakes internally. Persistence is the reliable signal instead:
                // a transient error is cleared by the next successful accept (errorCounter reset above), while
                // permanent damage keeps throwing. The short delay below stops a broken listener from
                // hot-spinning and makes maxErrorCount a meaningful time window rather than a microsecond burst.
                errorCounter++;
                if (errorCounter > maxErrorCount) {
                    VhLogger.Instance.LogError(ex,
                        "Too many unexpected errors in QUIC AcceptConnectionAsync. Waiting 60 seconds...");
                    await Task.Delay(TimeSpan.FromSeconds(60), ct).Vhc();
                }
            }
        }

        VhLogger.Instance.LogInformation("QUIC Listener has been stopped. LocalEp: {LocalEp}", VhLogger.Format(localEp));
    }

    private async Task AcceptStreams(IQuicConnection quicConnection, CancellationToken ct)
    {
        var remoteEp = quicConnection.RemoteEndPoint;

        try {
            while (!ct.IsCancellationRequested) {
                var stream = await quicConnection.AcceptInboundStreamAsync(ct).Vhc();
                _ = TryProcessStream(quicConnection, stream, ct);
            }
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(GeneralEventId.Request, ex,
                "QUIC connection has been closed. RemoteEp: {RemoteEp}",
                VhLogger.Format(remoteEp));
        }
        finally {
            try { await quicConnection.DisposeAsync().Vhc(); }
            catch { /* ignore */ }
        }
    }

    private async Task TryProcessStream(IQuicConnection quicConnection, Stream stream, CancellationToken ct)
    {
        var connection = new QuicStreamConnection(stream,
            localEndPoint: quicConnection.LocalEndPoint,
            remoteEndPoint: quicConnection.RemoteEndPoint,
            connectionName: "tunnel",
            isServer: true);

        var serverConnection = new ServerStreamConnection(connection) {
            ClientIp = quicConnection.RemoteEndPoint.Address
        };

        using var timeoutCts = new CancellationTokenSource(sessionManager.SessionOptions.TcpConnectTimeoutValue);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);
        await processNewConnection(serverConnection, connectCts.Token).Vhc();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        var tasks = _listeners.Select(x => x.DisposeAsync().AsTask()).ToList();
        await Task.WhenAll(tasks).Vhc();
        _listeners.Clear();
    }

    private sealed class QuicListenerEntry(IQuicListener listener, Task listenerTask, CancellationTokenSource cts)
        : IAsyncDisposable
    {
        public IQuicListener Listener => listener;
        public Task ListenerTask => listenerTask;

        public async ValueTask DisposeAsync()
        {
            await cts.TryCancelAsync().Vhc();
            await Listener.SafeDisposeAsync().Vhc();
            try { await listenerTask.Vhc(); }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Error in stopping QuicListener.");
            }
            cts.Dispose();
        }
    }
}
