using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Core.Server.Access;
using VpnHood.Net.Http3;
using VpnHood.Core.Server.ProbeResistance;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Connections;

namespace VpnHood.Core.Server.Listeners;

internal class QuicListenerHost(
    IQuicServer? quicServer,
    SessionManager sessionManager,
    UnauthenticatedConnections unauthenticatedConnections,
    CancellationToken cancellationToken,
    Func<IStreamConnection, CancellationToken, Task> processNewConnection)
    : IAsyncDisposable
{
    private readonly List<QuicListenerEntry> _listeners = [];
    private IReadOnlyList<CertificateHostName> _certificates = [];
    private readonly AsyncLock _disposeLock = new();
    private readonly Http3ServerOptions _http3Options = new() {
        ResponseHeaders = Http3UnauthorizedResponder.CreateHeaders
    };
    private bool _disposed;

    // only report listeners that are still running; a listener whose task has finished
    // (e.g. stopped due to an unrecoverable error) must not be advertised in the Hello response
    public IReadOnlyList<IPEndPoint> EndPoints =>
        _listeners.Where(x => x.IsListening).Select(x => x.Listener.LocalEndPoint).ToArray();

    public async Task<IReadOnlyList<ServerHostEndPointStatus>> Configure(
        IReadOnlyList<IPEndPoint> ipEndPoints,
        IReadOnlyList<CertificateHostName> certificates)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _certificates = certificates;

        // stop listeners that are no longer in the list
        foreach (var entry in _listeners
                     .Where(x => !x.IsListening ||
                                 !ipEndPoints.Any(ep => ep.Equals(x.Listener.LocalEndPoint))).ToArray()) {
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
                    // HTTP/3 owns idle and request deadlines; native expiry must not end a busy tunnel.
                    IdleTimeout = Timeout.InfiniteTimeSpan,
                    DefaultStreamErrorCode = Http3ErrorCode.InternalError,
                    DefaultCloseErrorCode = Http3ErrorCode.NoError,
                    ServerCertificateSelector = SelectCertificate
                };
                var listener = await quicServer.ListenAsync(listenerOptions, cancellationToken).Vhc();
                var entry = new QuicListenerEntry(listener, cancellationToken);
                entry.ListenerTask = ListenTask(entry, entry.Token);
                _listeners.Add(entry);
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

        return endPointStatuses;
    }

    private X509Certificate SelectCertificate(string? hostname)
    {
        var certificate =
            _certificates.SingleOrDefault(x => x.HostName.Equals(hostname, StringComparison.OrdinalIgnoreCase))
            ?? _certificates.First();

        return certificate.Certificate;
    }

    private async Task ListenTask(QuicListenerEntry entry, CancellationToken cancellationToken)
    {
        var listener = entry.Listener;
        var localEp = listener.LocalEndPoint;
        var errorCounter = 0;
        const int maxErrorCount = 200;

        try {
            while (!cancellationToken.IsCancellationRequested) {
                try {
                    var quicConnection = await listener.AcceptConnectionAsync(cancellationToken).Vhc();
                    entry.TrackConnection(AcceptStreams(quicConnection, cancellationToken));

                    // A successful accept proves the listener is healthy; isolated handshake failures
                    // must not accumulate into a persistent-listener failure.
                    errorCounter = 0;
                }
                catch (OperationCanceledException) {
                    break;
                }
                catch (ObjectDisposedException) {
                    break;
                }
                catch (Exception ex) {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    VhLogger.Instance.LogError(GeneralEventId.Request, ex,
                        "ServerHost could not AcceptQuicConnection. LocalEndPoint: {LocalEndPoint}, ErrorCounter: {ErrorCounter}",
                        localEp, errorCounter);

                    // MsQuic can report handshake faults and damaged listeners with the same exception.
                    // Back off repeated failures instead of hot-spinning while the host reconfigures.
                    if (++errorCounter > maxErrorCount) {
                        VhLogger.Instance.LogError(ex,
                            "Too many unexpected errors in QUIC AcceptConnectionAsync. Waiting 60 seconds...");
                        await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken).Vhc();
                    }
                }
            }
        }
        catch (OperationCanceledException) {
            // Cancellation during the failure backoff also follows the same cleanup path.
        }
        finally {
            // Stop advertising immediately, even if a connection is still finishing its bounded close.
            // An unexpectedly ended accept loop cancels its children without waiting for reconfiguration.
            // Cleanup must finish after listener cancellation has stopped acceptance.
            var closeListener = entry.CloseListener(CancellationToken.None).AsTask();
            try {
                await Task.WhenAll(closeListener, entry.WaitForConnections(CancellationToken.None)).Vhc();
            }
            finally {
                entry.ReleaseCancellation();
            }
            VhLogger.Instance.LogInformation("QUIC Listener has been stopped. LocalEp: {LocalEp}",
                VhLogger.Format(localEp));
        }
    }

    private async Task AcceptStreams(IQuicConnection quicConnection, CancellationToken cancellationToken)
    {
        try {
            await RunConnection(quicConnection, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            // Listener shutdown owns cancellation.
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(GeneralEventId.Request, ex,
                "QUIC connection has been closed. RemoteEp: {RemoteEp}",
                VhLogger.Format(quicConnection.RemoteEndPoint));
        }
    }

    private async Task RunConnection(IQuicConnection quicConnection, CancellationToken cancellationToken)
    {
        if (quicConnection is not IQuicServerConnection serverConnection) {
            await quicConnection.TryDisposeAsync(_http3Options.CloseTimeout).Vhc();
            throw new NotSupportedException("The QUIC provider must expose server stream control for HTTP/3.");
        }

        var budget = new Http3ConnectionBudget(serverConnection, unauthenticatedConnections,
            _http3Options.CloseTimeout);
        var filter = new Http3ProofFilter(sessionManager);
        var runner = new Http3TunnelRunner(budget, processNewConnection);
        var responder = new Http3UnauthorizedResponder();
        var front = new Http3ServerConnection(budget, _http3Options, filter, runner, responder);
        try {
            budget.Register();
            await front.Run(cancellationToken).Vhc();
        }
        finally {
            budget.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        // a second close waits for the first one's end
        using var scopeLock = await _disposeLock.LockAsync(CancellationToken.None).Vhc();
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        var tasks = _listeners.Select(x => x.DisposeAsync().AsTask()).ToList();
        await Task.WhenAll(tasks).Vhc();
        _listeners.Clear();
    }
}
