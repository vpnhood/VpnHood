using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Streams;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Connections;

namespace VpnHood.Core.Server.Listeners;

internal class TcpListenerHost(
    SessionManager sessionManager,
    CancellationToken cancellationToken,
    Func<IStreamConnection, CancellationToken, Task> processNewConnection)
{
    private readonly List<TcpListenerEntry> _listeners = [];
    private IReadOnlyList<CertificateHostName> _certificates = [];

    public IReadOnlyList<IPEndPoint> EndPoints =>
        _listeners.Select(x => (IPEndPoint)x.Listener.LocalEndpoint).ToArray();

    public async Task Configure(IPEndPoint[] ipEndPoints, IReadOnlyList<CertificateHostName> certificates)
    {
        _certificates = certificates;

        if (ipEndPoints.Length == 0)
            throw new ArgumentNullException(nameof(ipEndPoints), "No TcpEndPoint has been configured.");

        // stop listeners that are not in the new list
        foreach (var entry in _listeners
                     .Where(x => !ipEndPoints.Contains((IPEndPoint)x.Listener.LocalEndpoint)).ToArray()) {
            VhLogger.Instance.LogInformation("Stop listening on TcpEndPoint: {TcpEndPoint}",
                VhLogger.Format(entry.Listener.LocalEndpoint));
            await entry.StopAsync().Vhc();
            _listeners.Remove(entry);
        }

        if (_certificates.Count == 0 && ipEndPoints.Length > 0)
            throw new InvalidOperationException("No certificate has been configured for TCP.");

        // start new listeners
        foreach (var ipEndPoint in ipEndPoints) {
            // check already listening
            if (_listeners.Any(x => x.Listener.LocalEndpoint.Equals(ipEndPoint)))
                continue;

            try {
                VhLogger.Instance.LogInformation("Start listening on TcpEndPoint: {TcpEndPoint}",
                    VhLogger.Format(ipEndPoint));
                var tcpListener = new TcpListener(ipEndPoint);
                tcpListener.Start();
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var task = ListenTask(tcpListener, cts.Token);
                _listeners.Add(new TcpListenerEntry(tcpListener, task, cts));
            }
            catch (Exception ex) {
                VhLogger.Instance.LogInformation("Error listening on TcpEndPoint: {TcpEndPoint}",
                    VhLogger.Format(ipEndPoint));
                ex.Data.Add("TcpEndPoint", ipEndPoint);
                throw;
            }
        }

        // remove listeners whose task already finished (e.g. stopped due to too many errors)
        _listeners.RemoveAll(x => x.ListenerTask.IsCompleted);
    }

    private async Task ListenTask(TcpListener listener, CancellationToken ct)
    {
        var localEp = (IPEndPoint)listener.LocalEndpoint;
        var errorCounter = 0;
        const int maxErrorCount = 200;

        while (!ct.IsCancellationRequested) {
            try {
                var tcpClient = await listener.AcceptTcpClientAsync(ct).Vhc();

                var tcpKernelBufferSize = sessionManager.SessionOptions.TcpKernelBufferSize ??
                                          TunnelDefaults.ServerTcpKernelBufferSize;
                VhUtils.ConfigTcpClient(tcpClient,
                    sendBufferSize: tcpKernelBufferSize?.Send,
                    receiveBufferSize: tcpKernelBufferSize?.Receive);

                _ = TryProcessTcpClient(tcpClient, ct);
                errorCounter = 0;
            }
            catch (Exception) when (!listener.Server.IsBound) {
                break;
            }
            catch (OperationCanceledException) {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted) {
                errorCounter++;
            }
            catch (ObjectDisposedException) {
                errorCounter++;
            }
            catch (Exception ex) {
                if (ct.IsCancellationRequested)
                    break;

                errorCounter++;
                if (errorCounter > maxErrorCount) {
                    VhLogger.Instance.LogError(
                        "Too many unexpected errors in AcceptTcpClient. Stopping the Listener... LocalEndPint: {LocalEndPint}",
                        localEp);
                    break;
                }

                VhLogger.Instance.LogError(GeneralEventId.Request, ex,
                    "ServerHost could not AcceptTcpClient. LocalEndPint: {LocalEndPint}, ErrorCounter: {ErrorCounter}",
                    localEp, errorCounter);
            }
        }

        listener.Stop();
        listener.Dispose();
        VhLogger.Instance.LogInformation("TCP Listener has been stopped. LocalEp: {LocalEp}", VhLogger.Format(localEp));
    }

    private async Task TryProcessTcpClient(TcpClient tcpClient, CancellationToken ct)
    {
        try {
            await ProcessTcpClient(tcpClient, ct).Vhc();
        }
        catch (Exception ex) {
                VhLogger.Instance.LogDebug(GeneralEventId.Request, ex,
                    "ServerHost could not process this request. RemoteEp: {RemoteEp}, ClientIp: {ClientIp}, ConnectionId: {ConnectionId}",
                    VhLogger.Format(tcpClient.TryGetRemoteEndPoint()), ex.Data["ClientIp"], ex.Data["ConnectionId"]);

            tcpClient.Dispose();
        }
    }

    private async Task ProcessTcpClient(TcpClient tcpClient, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(sessionManager.SessionOptions.TcpConnectTimeoutValue);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);
        var connection = await CreateConnection(tcpClient, connectCts.Token).Vhc();
        await processNewConnection(connection, connectCts.Token).Vhc();
    }

    private async Task<IStreamConnection> CreateConnection(TcpClient tcpClient, CancellationToken ct)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.Request, "TLS Authenticating...");
        var sslStream = new SslStream(tcpClient.GetStream(), true);

        try {
            await sslStream.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions {
                    ClientCertificateRequired = false,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    ServerCertificateSelectionCallback = ServerCertificateSelectionCallback
                },
                ct).Vhc();

            var tcpConnection = new TcpStreamConnection(tcpClient, sslStream, connectionName: "tunnel", isServer: true);
            return tcpConnection;
        }
        catch (Exception) {
            await sslStream.SafeDisposeAsync().Vhc();
            throw;
        }
    }

    private X509Certificate ServerCertificateSelectionCallback(object sender, string? hostname)
    {
        var certificate =
            _certificates.SingleOrDefault(x => x.HostName.Equals(hostname, StringComparison.OrdinalIgnoreCase))
            ?? _certificates.First();

        return certificate.Certificate;
    }

    public async ValueTask StopAllAsync()
    {
        var tasks = _listeners.Select(x => x.StopAsync().AsTask()).ToList();
        await Task.WhenAll(tasks).Vhc();
        _listeners.Clear();
    }

    private sealed class TcpListenerEntry(TcpListener listener, Task listenerTask, CancellationTokenSource cts)
    {
        public TcpListener Listener => listener;
        public Task ListenerTask => listenerTask;

        public async ValueTask StopAsync()
        {
            try { await cts.TryCancelAsync().Vhc(); } catch { /* ignore */ }
            try { listener.Stop(); } catch { /* ignore */ }
            try { await listenerTask.Vhc(); }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Error in stopping TcpListener.");
            }
            cts.Dispose();
        }
    }
}
