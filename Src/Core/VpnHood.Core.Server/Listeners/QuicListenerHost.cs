using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.Access;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Connections;

namespace VpnHood.Core.Server.Listeners;

internal class QuicListenerHost(
    SessionManager sessionManager,
    CancellationToken cancellationToken,
    Func<IStreamConnection, CancellationToken, Task> processNewConnection)
    : IAsyncDisposable
{
    private readonly List<QuicListenerEntry> _listeners = [];
    private IReadOnlyList<CertificateHostName> _certificates = [];
    private bool _disposed;

    public IReadOnlyList<IPEndPoint> EndPoints =>
        _listeners.Select(x => x.Listener.LocalEndPoint).ToArray();

    public async Task<IReadOnlyList<ServerHostEndPointStatus>> Configure(
        IReadOnlyList<IPEndPoint> ipEndPoints, 
        IReadOnlyList<CertificateHostName> certificates)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _certificates = certificates;

        if (!QuicListener.IsSupported && ipEndPoints.Count > 0) {
            throw new NotSupportedException("QUIC is not supported on this platform.");
        }

        if (ipEndPoints.Any(x => x.Port == 0))
            throw new InvalidOperationException("QUIC port has not been specified.");

        // stop listeners that are no longer in the list
        foreach (var entry in _listeners
                     .Where(x => !ipEndPoints.Any(ep => ep.Equals(x.Listener.LocalEndPoint))).ToArray()) {
            VhLogger.Instance.LogInformation("Stop listening on QuicEndPoint: {QuicEndPoint}",
                VhLogger.Format(entry.Listener.LocalEndPoint));
            await entry.DisposeAsync().Vhc();
            _listeners.Remove(entry);
        }

        if (_certificates.Count == 0 && ipEndPoints.Count > 0)
            throw new InvalidOperationException("No certificate has been configured for QUIC.");

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
                var listenerOptions = new QuicListenerOptions {
                    ListenEndPoint = ipEndPoint,
                    ApplicationProtocols = [SslApplicationProtocol.Http3],
                    ConnectionOptionsCallback = QuicConnectionOptionsCallback
                };
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var listener = await QuicListener.ListenAsync(listenerOptions, cancellationToken).Vhc();
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

    private ValueTask<QuicServerConnectionOptions> QuicConnectionOptionsCallback(
        QuicConnection quicConnection, SslClientHelloInfo sslClientHelloInfo, CancellationToken ct)
    {
        var options = new QuicServerConnectionOptions {
            DefaultStreamErrorCode = 0,
            DefaultCloseErrorCode = 0,
            IdleTimeout = sessionManager.SessionOptions.ChannelIdleTimeoutValue,
            ServerAuthenticationOptions = new SslServerAuthenticationOptions {
                ServerCertificateSelectionCallback = ServerCertificateSelectionCallback,
                ApplicationProtocols = [SslApplicationProtocol.Http3], // just to look normal, we use HTTP 1.1 actually
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                ClientCertificateRequired = false,
                EncryptionPolicy = EncryptionPolicy.RequireEncryption,
                EnabledSslProtocols = SslProtocols.Tls13
            }
        };
        return new ValueTask<QuicServerConnectionOptions>(options);
    }

    private X509Certificate ServerCertificateSelectionCallback(object sender, string? hostname)
    {
        var certificate =
            _certificates.SingleOrDefault(x => x.HostName.Equals(hostname, StringComparison.OrdinalIgnoreCase))
            ?? _certificates.First();

        return certificate.Certificate;
    }

    private async Task ListenTask(QuicListener listener, CancellationToken ct)
    {
        var localEp = listener.LocalEndPoint;
        var errorCounter = 0;
        const int maxErrorCount = 200;

        while (!ct.IsCancellationRequested) {
            try {
                var quicConnection = await listener.AcceptConnectionAsync(ct).Vhc();
                _ = AcceptStreams(quicConnection, ct);
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

                errorCounter++;
                if (errorCounter > maxErrorCount) {
                    VhLogger.Instance.LogError(
                        "Too many unexpected errors in QUIC AcceptConnectionAsync. Stopping the Listener... LocalEndPint: {LocalEndPint}",
                        localEp);
                    break;
                }

                VhLogger.Instance.LogError(GeneralEventId.Request, ex,
                    "ServerHost could not AcceptQuicConnection. LocalEndPint: {LocalEndPint}, ErrorCounter: {ErrorCounter}",
                    localEp, errorCounter);
            }
        }

        VhLogger.Instance.LogInformation("QUIC Listener has been stopped. LocalEp: {LocalEp}", VhLogger.Format(localEp));
    }

    private async Task AcceptStreams(QuicConnection quicConnection, CancellationToken ct)
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

    private async Task TryProcessStream(QuicConnection quicConnection, QuicStream stream, CancellationToken ct)
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

    private sealed class QuicListenerEntry(QuicListener listener, Task listenerTask, CancellationTokenSource cts)
        : IAsyncDisposable
    {
        public QuicListener Listener => listener;
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
