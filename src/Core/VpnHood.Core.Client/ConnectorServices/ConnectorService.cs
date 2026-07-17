using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Memory;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client.ConnectorServices;

/// <summary>
/// Manages the transport layer for outbound connections to the VPN server.
/// </summary>
/// <remarks>
/// Supports two transport backends:
/// <list type="bullet">
///   <item><term>TCP + TLS</term><description>Each tunnel request opens a new TLS-authenticated TCP connection via <see cref="TcpStreamConnectionFactory"/>. Connections that complete a request can be returned to the reuse pool.</description></item>
///   <item><term>QUIC</term><description>Connections are opened as bidirectional streams on a pooled <see cref="System.Net.Quic.QuicConnection"/> via <see cref="QuicStreamConnectionFactory"/>. QUIC streams are independent and are NOT added to the reuse pool � each stream maps 1:1 to a request lifetime.</description></item>
/// </list>
/// HTTP framing (POST header or WebSocket upgrade) is applied on top of the raw stream before
/// returning it � the server's <c>ProcessNewConnection</c> expects an HTTP/1.1 request regardless
/// of the underlying transport.
/// <para>
/// Our QUIC is a custom protocol, NOT HTTP/3. We use it purely as a transport layer,
/// with the same binary request/response framing as TCP. <c>SslApplicationProtocol.Http3</c>
/// is used only as the ALPN token during the TLS handshake.
/// </para>
/// Owned by <see cref="RequestSender"/> � created from <see cref="ConnectorServiceOptions"/>,
/// exposed via <see cref="RequestSender.ConnectorService"/>, and disposed by <see cref="RequestSender.Dispose"/>.
/// </remarks>
internal class ConnectorService : IDisposable
{
    private const bool UseBuffer = true;
    private readonly ConcurrentQueue<IdleConnectionItem> _idleConnectionItems = new();
    private readonly HashSet<ReusableStreamConnection> _sharedConnections = new(50); // for preventing reuse
    private readonly TcpStreamConnectionFactory _tcpConnectionFactory;
    private readonly QuicStreamConnectionFactory? _quicConnectionFactory; // null when the socket factory has no QUIC support
    private TimeSpan _idleConnectionTimeout;
    private readonly Job _cleanupJob;
    private bool _disposed;
    private bool _useWebSocket;

    public ConnectorStat Stat { get; }
    public int ProtocolVersion { get; private set; } = 8;
    public VpnEndPoint VpnEndPoint { get; init; }
    public TimeSpan RequestTimeout { get; set; }
    public bool UseQuic { get; set; }


    public IPEndPoint? QuicEndPoint {
        get => _quicConnectionFactory?.QuicEndPoint;
        set {
            _quicConnectionFactory?.QuicEndPoint = value;
        }
    }

    public ConnectorService(ConnectorServiceOptions options)
    {
        AllowChannelReuse = options.AllowChannelReuse;
        Stat = new ConnectorStat(() => _idleConnectionItems.Count);
        VpnEndPoint = options.VpnEndPoint;
        RequestTimeout = options.RequestTimeout;
        _cleanupJob = new Job(Cleanup, "ConnectorCleanup");

        _tcpConnectionFactory = new TcpStreamConnectionFactory(
            options.SocketFactory,
            options.ProxyConnector,
            options.VpnEndPoint,
            UserCertificateValidationCallback);

        _quicConnectionFactory = options.SocketFactory.IsQuicSupported
            ? new QuicStreamConnectionFactory(
                options.SocketFactory.CreateQuicClient(),
                options.VpnEndPoint,
                UserCertificateValidationCallback)
            : null;

        if (_quicConnectionFactory != null)
            _quicConnectionFactory.PanicRecycled += QuicConnectionFactory_PanicRecycled;

        // after quic (propagates to the QUIC factory if present)
        _idleConnectionTimeout = TimeSpan.FromSeconds(30).WhenNoDebugger();
    }

    public void Init(int protocolVersion, byte[]? serverSecret, TimeSpan channelIdleTimeout, bool useWebSocket,
        TimeSpan requestTimeout, bool useQuic, IPEndPoint? quicEndPoint)
    {
        _ = serverSecret;
        ProtocolVersion = protocolVersion;
        _idleConnectionTimeout = channelIdleTimeout;
        _quicConnectionFactory?.IdleConnectionTimeout = channelIdleTimeout;
        _useWebSocket = useWebSocket;
        RequestTimeout = requestTimeout;
        UseQuic = useQuic;
        QuicEndPoint = quicEndPoint;

    }

    private static string BuildRequestPath(string? pathBase)
    {
        // the URL is /{pathBase}/{guid}
        var urlBuilder = new StringBuilder("/");
        if (!string.IsNullOrEmpty(pathBase)) {
            urlBuilder.Append(pathBase.Trim('/'));
            urlBuilder.Append('/');
        }
        urlBuilder.Append(Guid.NewGuid());
        return urlBuilder.ToString();
    }

    private static string BuildPostRequest(
        string hostName, string? pathBase,
        TunnelStreamType streamType, int protocolVersion,
        int contentLength, string connectionId)
    {
        // write HTTP request
        var headerBuilder = new StringBuilder()
            .Append($"POST {BuildRequestPath(pathBase)} HTTP/1.1\r\n")
            .Append($"Host: {hostName}\r\n")
            .Append($"Content-Length: {contentLength}\r\n")
            .Append("Content-Type: application/octet-stream\r\n")
            .Append("User-Agent: Hood\r\n")
            .Append($"X-Buffered: {UseBuffer}\r\n")
            .Append($"X-ProtocolVersion: {protocolVersion}\r\n")
            .Append($"X-ConnectionId: {connectionId}\r\n")
            .Append($"X-BinaryStream: {streamType}\r\n")
            .Append("\r\n");

        var header = headerBuilder.ToString();
        return header;
    }

    private async Task<IStreamConnection> CreateWebSocketConnection(
        IStreamConnection streamConnection,
        CancellationToken cancellationToken)
    {
        var webSocketKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

        // write HTTP request
        var headerBuilder = new StringBuilder()
            .Append($"GET /{BuildRequestPath(VpnEndPoint.PathBase)} HTTP/1.1\r\n")
            .Append($"Host: {VpnEndPoint.HostName}\r\n")
            .Append($"X-Buffered: {UseBuffer}\r\n")
            .Append($"X-ProtocolVersion: {ProtocolVersion}\r\n")
            .Append($"X-ConnectionId: {streamConnection.ConnectionId}\r\n")
            .Append("Upgrade: websocket\r\n")
            .Append("Connection: Upgrade\r\n")
            .Append("Sec-WebSocket-Version: 13\r\n")
            .Append($"Sec-WebSocket-Key: {webSocketKey}\r\n")
            .Append("\r\n");

        // Send header and wait for its response
        await streamConnection.Stream.WriteAsync(Encoding.UTF8.GetBytes(headerBuilder.ToString()), cancellationToken).Vhc();

        // wait for response and websocket upgrade if needed before sending any data because GET can not have body
        var responseMessage = await HttpUtils.ReadResponse(streamConnection.Stream, cancellationToken).Vhc();
        if (responseMessage.StatusCode != HttpStatusCode.SwitchingProtocols)
            throw new Exception("Unexpected response.");

        // create a client stream
        var webSocketStream = new WebSocketStream(streamConnection.Stream, streamConnection.ConnectionId, UseBuffer, isServer: false);
        var webSocketConnection = new StreamConnectionDecorator(streamConnection, webSocketStream) {
            RequireHttpResponse = false
        };

        // create a reusable connection
        if (!AllowChannelReuse)
            return webSocketConnection;

        // If reuse is allowed, add the client stream to the shared collection
        var reusableConnection = new ReusableStreamConnection(webSocketConnection, ConnectionReuseCallback);
        lock (_sharedConnections)
            _sharedConnections.Add(reusableConnection);
        return reusableConnection;
    }

    private async Task<IStreamConnection> CreateSimpleConnection(IStreamConnection streamConnection, int contentLength,
        CancellationToken cancellationToken)
    {
        // write HTTP request
        var header = BuildPostRequest(hostName: VpnEndPoint.HostName, pathBase: VpnEndPoint.PathBase,
            TunnelStreamType.None, ProtocolVersion, contentLength: contentLength, connectionId: streamConnection.ConnectionId);

        // Send header and wait for its response
        await streamConnection.Stream.WriteAsync(Encoding.UTF8.GetBytes(header), cancellationToken).Vhc();
        streamConnection.RequireHttpResponse = true;
        return streamConnection;
    }

    private Task<IStreamConnection> CreateHttpConnection(IStreamConnection streamConnection, int contentLength, CancellationToken cancellationToken)
    {
        return _useWebSocket
            ? CreateWebSocketConnection(streamConnection, cancellationToken) // WebSocket connection
            : CreateSimpleConnection(streamConnection, contentLength, cancellationToken); // Simple HTTP connection
    }

    // Gate on FRESH server connections (pooled reuse is not gated). Every fresh connection is a full
    // QUIC-stream open or TCP+TLS(+WebSocket) handshake whose native cost (Security/Network.framework
    // state, kernel socket buffers) lives outside every managed budget. A churn burst — e.g. a browser
    // re-opening dozens of flows after a tunnel stall — otherwise fires 10-20 handshakes simultaneously;
    // on iOS (~52 MB jetsam limit) such a burst spiked phys_footprint several MB inside 500 ms and killed
    // the extension even with the download-ingest and SYN-admission gates active (2026-07-11 on-device:
    // 232 fresh connections in a 12-min session, 11 inside one burst). Browsers cap ~6 per host for the
    // same reason; the tunnel talks to ONE server, and waiters usually pick up a pooled connection freed
    // meanwhile, so a small cap is safe. Static: the budget being protected (process footprint) is
    // process-wide, and a reconnect can briefly overlap two ConnectorService instances.
    private static readonly SemaphoreSlim ConnectGate =
        new(OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst() ? 3 : 8);

    // While the process footprint is critical, hold fresh establishment entirely (aligned with the QUIC
    // ingest gate and the TCP-stack admission gate at 42 MB): in the jammed-tunnel state native handshake
    // transients do not drain, so even serialized creates ratchet the footprint. Flows either get a pooled
    // connection, wait it out, or fail on their own RequestTimeout — all better than a jetsam kill.
    // No memory reader installed (ProcessFootprintMb is null) → comparison is false → no hold.
    private const double ConnectMemoryLimitMb = 41.0;

    public async Task<IStreamConnection> GetConnectionToServer(string streamId, int contentLength,
        Action? onConnectAttempt, CancellationToken cancellationToken)
    {
        // Create raw stream connection from appropriate factory
        if (UseQuic && _quicConnectionFactory == null)
            throw new InvalidOperationException("QUIC is not supported by the current socket factory.");

        await ConnectGate.WaitAsync(cancellationToken).Vhc();
        try {
            while (VhMemory.Instance.GetInfo().ProcessFootprintMb >= ConnectMemoryLimitMb)
                await Task.Delay(100, cancellationToken).Vhc();

            var rawConnection = UseQuic
                ? await _quicConnectionFactory!.CreateConnection(streamId, cancellationToken).Vhc()
                : await _tcpConnectionFactory.CreateConnection(streamId, onConnectAttempt, cancellationToken).Vhc();

            try {
                // Apply HTTP framing on top of the raw connection
                var connection = await CreateHttpConnection(rawConnection, contentLength, cancellationToken).Vhc();
                lock (Stat) Stat.CreatedConnectionCount++;
                return connection;
            }
            catch {
                rawConnection.Dispose();
                throw;
            }
        }
        finally {
            ConnectGate.Release();
        }
    }

    public ReusableStreamConnection? GetFreeConnection()
    {
        // Kill all expired client streams
        while (_idleConnectionItems.TryDequeue(out var queueItem)) {
            if (!queueItem.IsExpired)
                return queueItem.StreamConnection;

            queueItem.StreamConnection.PreventReuse();
            queueItem.StreamConnection.Dispose();
        }

        return null;
    }

    private void ConnectionReuseCallback(ReusableStreamConnection streamConnection)
    {
        // Check if the connector service is disposed
        if (_disposed || !AllowChannelReuse) {
            VhLogger.Instance.LogDebug(GeneralEventId.Stream,
                "Disposing the reused client stream because the connector service is either disposed or reuse is no longer allowed. " +
                "ConnectionId: {ConnectionId}", streamConnection.ConnectionId);

            streamConnection.PreventReuse();
            streamConnection.Dispose();
            return;
        }

        _idleConnectionItems.Enqueue(new IdleConnectionItem {
            IdleConnectionTimeout = _idleConnectionTimeout,
            StreamConnection = streamConnection
        });
    }

    private ValueTask Cleanup(CancellationToken cancellationToken)
    {
        // Kill all expired client streams
        while (_idleConnectionItems.TryPeek(out var queueItem) && queueItem.IsExpired) {
            if (_idleConnectionItems.TryDequeue(out queueItem)) {
                queueItem.StreamConnection.PreventReuse();
                queueItem.StreamConnection.Dispose();
            }
        }

        // remove unconnected client streams from shared collection to reduce memory usage
        // it is just clearing the references, not disposing the streams.
        // The owner of the stream is responsible for disposing it
        lock (_sharedConnections)
            _sharedConnections.RemoveWhere(x => !x.Connected);

        return ValueTask.CompletedTask;
    }

    private void PreventReuseChannel()
    {
        lock (_sharedConnections) {
            foreach (var connection in _sharedConnections)
                connection.PreventReuse();

            // release all free client streams
            while (_idleConnectionItems.TryDequeue(out var queueItem)) {
                queueItem.StreamConnection.PreventReuse();
                queueItem.StreamConnection.Dispose();
            }
            _idleConnectionItems.Clear();
        }
    }

    public bool AllowChannelReuse {
        get;
        set {
            if (!value)
                PreventReuseChannel();
            field = value;
        }
    }

    private bool UserCertificateValidationCallback(object sender, X509Certificate? certificate, X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        if (certificate == null)
            return false;

        if (sslPolicyErrors == SslPolicyErrors.None)
            return true;

        var ret = VpnEndPoint.CertificateHash?.SequenceEqual(certificate.GetCertHash()) == true;
        return ret;
    }

    private void QuicConnectionFactory_PanicRecycled(object? sender, EventArgs e)
    {
        VhLogger.Instance.LogWarning(GeneralEventId.Request,
            "ConnectorService: QUIC pool panic recycle triggered. Clearing all idle shared connections.");
        PreventReuseChannel();
    }

    private void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        if (disposing) {
            // Stop the cleanup job
            _cleanupJob.Dispose();

            // Dispose connection factories
            _tcpConnectionFactory.Dispose();
            if (_quicConnectionFactory != null) {
                _quicConnectionFactory.PanicRecycled -= QuicConnectionFactory_PanicRecycled;
                _quicConnectionFactory.DisposeAsync().VhBlock();
            }

            // Prevent reuse of client streams
            PreventReuseChannel();

            // Kill and remove all owned client streams
            while (_idleConnectionItems.TryDequeue(out var queueItem)) {
                queueItem.StreamConnection.PreventReuse();
                queueItem.StreamConnection.Dispose();
            }
            _idleConnectionItems.Clear();

            // remove all shared client streams
            lock (_sharedConnections)
                _sharedConnections.Clear();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private class IdleConnectionItem
    {
        public required TimeSpan IdleConnectionTimeout { get; init; }
        public required ReusableStreamConnection StreamConnection { get; init; }
        private DateTime EnqueueTime { get; } = FastDateTime.Now;
        public bool IsExpired =>
            EnqueueTime + IdleConnectionTimeout <= FastDateTime.Now ||
            !StreamConnection.Connected;
    }

}