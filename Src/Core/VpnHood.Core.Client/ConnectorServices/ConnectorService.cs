using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
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
///   <item><term>QUIC</term><description>Connections are opened as bidirectional streams on a pooled <see cref="System.Net.Quic.QuicConnection"/> via <see cref="QuicStreamConnectionFactory"/>. QUIC streams are independent and are NOT added to the reuse pool — each stream maps 1:1 to a request lifetime.</description></item>
/// </list>
/// HTTP framing (POST header or WebSocket upgrade) is applied on top of the raw stream before
/// returning it — the server's <c>ProcessNewConnection</c> expects an HTTP/1.1 request regardless
/// of the underlying transport.
/// <para>
/// Our QUIC is a custom protocol, NOT HTTP/3. We use it purely as a transport layer,
/// with the same binary request/response framing as TCP. <c>SslApplicationProtocol.Http3</c>
/// is used only as the ALPN token during the TLS handshake.
/// </para>
/// Owned by <see cref="RequestSender"/> — created from <see cref="ConnectorServiceOptions"/>,
/// exposed via <see cref="RequestSender.ConnectorService"/>, and disposed by <see cref="RequestSender.Dispose"/>.
/// </remarks>
internal class ConnectorService : IDisposable
{
    private const bool UseBuffer = true;
    private readonly ConcurrentQueue<ReusableConnectionItem> _freeReusableStreamConnectionItems = new();
    private readonly HashSet<ReusableStreamConnection> _sharedStreamConnections = new(50); // for preventing reuse
    private readonly Job _cleanupJob;
    private int _isDisposed;
    private bool _useWebSocket;
    private readonly TcpStreamConnectionFactory _tcpStreamConnectionFactory;
    private readonly QuicStreamConnectionFactory _quicStreamConnectionFactory;

    public ConnectorStat Stat { get; }
    public TimeSpan StreamReuseTimeout { get; private set; }
    public int ProtocolVersion { get; private set; } = 8;
    public VpnEndPoint VpnEndPoint { get; init; }
    public TimeSpan RequestTimeout { get; set; }
    public bool UseQuic { get; set; }

    public IPEndPoint? QuicEndPoint {
        get => _quicStreamConnectionFactory.QuicEndPoint;
        set => _quicStreamConnectionFactory.QuicEndPoint = value;
    }

    public ConnectorService(ConnectorServiceOptions options)
    {
        AllowStreamConnectionReuse = options.AllowTcpReuse;
        Stat = new ConnectorStat(() => _freeReusableStreamConnectionItems.Count);
        StreamReuseTimeout = TimeSpan.FromSeconds(30).WhenNoDebugger();
        VpnEndPoint = options.VpnEndPoint;
        RequestTimeout = options.RequestTimeout;
        _cleanupJob = new Job(Cleanup, "ConnectorCleanup");

        _tcpStreamConnectionFactory = new TcpStreamConnectionFactory(
            options.SocketFactory,
            options.ProxyEndPointManager,
            options.VpnEndPoint,
            UserCertificateValidationCallback);

        _quicStreamConnectionFactory = new QuicStreamConnectionFactory(
            options.VpnEndPoint,
            UserCertificateValidationCallback);
    }

    public void Init(int protocolVersion, byte[]? serverSecret, TimeSpan tcpReuseTimeout, bool useWebSocket,
        TimeSpan requestTimeout, bool useQuic, IPEndPoint? quicEndPoint)
    {
        _ = serverSecret;
        ProtocolVersion = protocolVersion;
        StreamReuseTimeout = tcpReuseTimeout;
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
        if (!AllowStreamConnectionReuse)
            return webSocketConnection;

        // If reuse is allowed, add the client stream to the shared collection
        var reusableConnection = new ReusableStreamConnection(webSocketConnection, ConnectionReuseCallback);
        lock (_sharedStreamConnections)
            _sharedStreamConnections.Add(reusableConnection);
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

    public async Task<IStreamConnection> GetConnectionToServer(string streamId, int contentLength,
        Action? onConnectAttempt, CancellationToken cancellationToken)
    {
        // Create raw stream connection from appropriate factory
        var rawConnection = UseQuic
            ? await _quicStreamConnectionFactory.CreateConnection(streamId, cancellationToken).Vhc()
            : await _tcpStreamConnectionFactory.CreateConnection(streamId, onConnectAttempt, cancellationToken).Vhc();

        // Apply HTTP framing on top of the raw connection
        var connection = await CreateHttpConnection(rawConnection, contentLength, cancellationToken).Vhc();
        lock (Stat) Stat.CreatedConnectionCount++;
        return connection;
    }

    public ReusableStreamConnection? GetFreeConnection()
    {
        // Kill all expired client streams
        while (_freeReusableStreamConnectionItems.TryDequeue(out var queueItem)) {
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
        if (_isDisposed != 0 || !AllowStreamConnectionReuse) {
            VhLogger.Instance.LogDebug(GeneralEventId.Stream,
                "Disposing the reused client stream because the connector service is either disposed or reuse is no longer allowed. " +
                "ConnectionId: {ConnectionId}", streamConnection.ConnectionId);

            streamConnection.PreventReuse();
            streamConnection.Dispose();
            return;
        }

        _freeReusableStreamConnectionItems.Enqueue(new ReusableConnectionItem {
            IdleTimeout = StreamReuseTimeout,
            StreamConnection = streamConnection
        });
    }

    private ValueTask Cleanup(CancellationToken cancellationToken)
    {
        // Kill all expired client streams
        while (_freeReusableStreamConnectionItems.TryPeek(out var queueItem) && queueItem.IsExpired) {
            if (_freeReusableStreamConnectionItems.TryDequeue(out queueItem)) {
                queueItem.StreamConnection.PreventReuse();
                queueItem.StreamConnection.Dispose();
            }
        }

        // remove unconnected client streams from shared collection to reduce memory usage
        // it is just clearing the references, not disposing the streams.
        // The owner of the stream is responsible for disposing it
        lock (_sharedStreamConnections)
            _sharedStreamConnections.RemoveWhere(x => !x.Connected);

        return ValueTask.CompletedTask;
    }

    private void PreventReuseSharedStreamConnections()
    {
        lock (_sharedStreamConnections) {
            foreach (var connection in _sharedStreamConnections)
                connection.PreventReuse();

            // release all free client streams
            while (_freeReusableStreamConnectionItems.TryDequeue(out var queueItem)) {
                queueItem.StreamConnection.PreventReuse();
                queueItem.StreamConnection.Dispose();
            }
            _freeReusableStreamConnectionItems.Clear();
        }
    }

    public bool AllowStreamConnectionReuse {
        get;
        set {
            if (!value)
                PreventReuseSharedStreamConnections();
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


    private void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
            return;

        if (disposing) {
            // Stop the cleanup job
            _cleanupJob.Dispose();

            // Dispose connection factories
            _tcpStreamConnectionFactory.Dispose();
            _quicStreamConnectionFactory.DisposeAsync().VhBlock();

            // Prevent reuse of client streams
            PreventReuseSharedStreamConnections();

            // Kill and remove all owned client streams
            while (_freeReusableStreamConnectionItems.TryDequeue(out var queueItem)) {
                queueItem.StreamConnection.PreventReuse();
                queueItem.StreamConnection.Dispose();
            }
            _freeReusableStreamConnectionItems.Clear();

            // remove all shared client streams
            lock (_sharedStreamConnections)
                _sharedStreamConnections.Clear();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private class ReusableConnectionItem
    {
        public required TimeSpan IdleTimeout { get; init; }
        public required ReusableStreamConnection StreamConnection { get; init; }
        private DateTime EnqueueTime { get; } = FastDateTime.Now;
        public bool IsExpired =>
            EnqueueTime + IdleTimeout <= FastDateTime.Now ||
            !StreamConnection.Connected;
    }

}