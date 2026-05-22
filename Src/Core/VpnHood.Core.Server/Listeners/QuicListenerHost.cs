using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Connections;

namespace VpnHood.Core.Server.Listeners;

internal class QuicListenerHost(
    SessionManager sessionManager,
    CancellationToken cancellationToken,
    Func<IConnection, CancellationToken, Task> processNewConnection)
{
    private readonly List<QuicListenerEntry> _listeners = [];
    private IReadOnlyList<CertificateHostName> _certificates = [];

    public IReadOnlyList<IPEndPoint> EndPoints =>
        _listeners.Select(x => x.Listener.LocalEndPoint).ToArray();

    public async Task Configure(IPEndPoint[] ipEndPoints, IReadOnlyList<CertificateHostName> certificates)
    {
        _certificates = certificates;

        if (ipEndPoints.Any(x => x.Port == 0))
            throw new InvalidOperationException("QUIC port has not been specified.");

        // stop listeners that are no longer in the list
        foreach (var entry in _listeners
                     .Where(x => !ipEndPoints.Any(ep => ep.Equals(x.Listener.LocalEndPoint))).ToArray()) {
            VhLogger.Instance.LogInformation("Stop listening on QuicEndPoint: {QuicEndPoint}",
                VhLogger.Format(entry.Listener.LocalEndPoint));
            await entry.StopAsync().Vhc();
            _listeners.Remove(entry);
        }

        if (!QuicListener.IsSupported || !QuicConnection.IsSupported) {
            if (ipEndPoints.Length > 0)
                VhLogger.Instance.LogWarning("QUIC is not supported on this platform.");
            return;
        }

        if (_certificates.Count == 0 && ipEndPoints.Length > 0)
            throw new InvalidOperationException("No certificate has been configured for QUIC.");

        // start new listeners
        foreach (var ipEndPoint in ipEndPoints) {
            if (_listeners.Any(x => x.Listener.LocalEndPoint.Equals(ipEndPoint)))
                continue;

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
            }
            catch (Exception ex) {
                ex.Data.Add("QuicEndPoint", ipEndPoint);
                throw;
            }
        }

        // remove listeners whose task already finished (e.g. stopped due to unexpected error)
        _listeners.RemoveAll(x => x.ListenerTask.IsCompleted);
    }

    private ValueTask<QuicServerConnectionOptions> QuicConnectionOptionsCallback(
        QuicConnection quicConnection, SslClientHelloInfo sslClientHelloInfo, CancellationToken ct)
    {
        var options = new QuicServerConnectionOptions {
            DefaultStreamErrorCode = 0,
            DefaultCloseErrorCode = 0,
            IdleTimeout = sessionManager.SessionOptions.TcpReuseTimeoutValue,
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
        var localEp = (IPEndPoint)listener.LocalEndPoint;
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

        await listener.DisposeAsync();
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

        var serverConnection = new ServerConnection(connection) {
            ClientIp = quicConnection.RemoteEndPoint.Address
        };

        using var timeoutCts = new CancellationTokenSource(sessionManager.SessionOptions.TcpConnectTimeoutValue);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);
        await processNewConnection(serverConnection, connectCts.Token).Vhc();
    }

    public async ValueTask StopAllAsync()
    {
        var tasks = _listeners.Select(x => x.StopAsync().AsTask()).ToList();
        await Task.WhenAll(tasks).Vhc();
        _listeners.Clear();
    }

    private sealed class QuicListenerEntry(QuicListener listener, Task listenerTask, CancellationTokenSource cts)
    {
        public QuicListener Listener => listener;
        public Task ListenerTask => listenerTask;

        public async ValueTask StopAsync()
        {
            try { await cts.TryCancelAsync().Vhc(); } catch { /* ignore */ }
            try { await Listener.DisposeAsync().Vhc(); } catch { /* ignore */ }
            try { await listenerTask.Vhc(); }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Error in stopping QuicListener.");
            }
            cts.Dispose();
        }
    }
}
