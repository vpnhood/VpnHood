using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Jobs;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Memory;
using VpnHood.Net.Toolkit.Utils;
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
///   <item><term>QUIC</term><description>Connections are opened as bidirectional streams on a pooled <see cref="System.Net.Quic.QuicConnection"/> via <see cref="QuicStreamConnectionFactory"/>. As with TCP, those framed as WebSocket can be returned to the reuse pool.</description></item>
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
/// exposed via <see cref="RequestSender.ConnectorService"/>, and disposed by <see cref="RequestSender.DisposeAsync"/>.
/// </remarks>
internal class ConnectorService : IAsyncDisposable
{
    private const bool UseBuffer = true;
    private readonly ConcurrentQueue<IdleConnectionItem> _idleConnectionItems = new();
    private readonly TcpStreamConnectionFactory _tcpConnectionFactory;
    private readonly QuicStreamConnectionFactory? _quicConnectionFactory; // null when the socket factory has no QUIC support
    private TimeSpan _idleConnectionTimeout;
    private readonly Job _cleanupJob;
    private readonly AsyncLock _disposeLock = new();
    private bool _disposed;
    private bool _useWebSocket;
    private int _generation; // RetireConnections moves it on: a connection made before it is not reused

    // a connection kept too long draws a firewall's notice, so each retires at a random age between these: random,
    // so they do not retire together, as that would be a pattern too. Settable for tests
    internal static TimeSpan MinConnectionLifespan { get; set; } = TimeSpan.FromMinutes(5);
    internal static TimeSpan MaxConnectionLifespan { get; set; } = TimeSpan.FromMinutes(10);

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
            UserCertificateValidationCallback,
            options.TcpPacketChannelKernelBufferSize);

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
        IStreamConnection streamConnection, ReuseConnectionCallback? reuseCallback,
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
        return new ReusableStreamConnection(streamConnection, webSocketStream, reuseCallback) {
            RequireHttpResponse = false
        };
    }

    // the connection's own for its whole life, as each reuse passes it on
    private ReuseConnectionCallback CreateReuseCallback()
    {
        var generation = _generation;
        var expirationTime = FastDateTime.UtcNow + TimeSpan.FromTicks(
            Random.Shared.NextInt64(MinConnectionLifespan.Ticks, MaxConnectionLifespan.Ticks + 1));
        return connection => ReturnConnection(connection, generation, expirationTime);
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

    private Task<IStreamConnection> CreateHttpConnection(IStreamConnection streamConnection, int contentLength,
        ReuseConnectionCallback? reuseCallback, CancellationToken cancellationToken)
    {
        return _useWebSocket
            ? CreateWebSocketConnection(streamConnection, reuseCallback, cancellationToken) // WebSocket connection
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

    // a wait this long before a connection even starts stalls the request that asked for it, and the log would
    // otherwise show only a slow server. Debug: making such waits in a burst is the gate's job, so they are no
    // fault, and the server finder alone checks more endpoints at once than the gate admits
    private static readonly TimeSpan LongConnectWaitTime = TimeSpan.FromMilliseconds(500);

    public async Task<IStreamConnection> GetConnectionToServer(string streamId, int contentLength,
        bool isTcpPacketChannel, Action? onConnectAttempt, CancellationToken cancellationToken)
    {
        // Create raw stream connection from appropriate factory
        if (UseQuic && _quicConnectionFactory == null)
            throw new InvalidOperationException("QUIC is not supported by the current socket factory.");

        var waitStartTime = Stopwatch.GetTimestamp();
        await ConnectGate.WaitAsync(cancellationToken).Vhc();
        try {
            var slotWaitTime = Stopwatch.GetElapsedTime(waitStartTime);
            while (VhMemory.Instance.GetInfo().ProcessFootprintMb >= ConnectMemoryLimitMb)
                await Task.Delay(100, cancellationToken).Vhc();

            var waitTime = Stopwatch.GetElapsedTime(waitStartTime);
            if (waitTime >= LongConnectWaitTime)
                VhLogger.Instance.LogDebug(GeneralEventId.Request,
                    "A new connection to the server had to wait. WaitMs: {WaitMs}, SlotWaitMs: {SlotWaitMs}, " +
                    "FreeSlots: {FreeSlots}, ConnectionId: {ConnectionId}",
                    (long)waitTime.TotalMilliseconds, (long)slotWaitTime.TotalMilliseconds,
                    ConnectGate.CurrentCount, streamId);

            // before the connection is made, so one a network change overtakes is retired with the old ones. Packet
            // channels have transport-specific socket sizing and must never return to the generic proxy/control pool;
            // this also keeps a finite channel lifespan meaningful
            var reuseCallback = AllowChannelReuse && !isTcpPacketChannel ? CreateReuseCallback() : null;

            var rawConnection = UseQuic
                ? await _quicConnectionFactory!.CreateConnection(streamId, cancellationToken).Vhc()
                : await _tcpConnectionFactory.CreateConnection(
                    streamId, isTcpPacketChannel, onConnectAttempt, cancellationToken).Vhc();

            try {
                // Apply HTTP framing on top of the raw connection
                var connection = await CreateHttpConnection(rawConnection, contentLength, reuseCallback,
                    cancellationToken).Vhc();
                lock (Stat) Stat.CreatedConnectionCount++;
                return connection;
            }
            catch {
                // not waited for: the gate's slot would be held while a QUIC stream's close waits for the peer
                _ = rawConnection.TryDisposeAsync();
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
            if (!IsExpired(queueItem))
                return queueItem.StreamConnection;

            Close(queueItem.StreamConnection);
        }

        return null;
    }

    // a connection whose logical stream ended cleanly, back for another request
    private void ReturnConnection(ReusableStreamConnection connection, int generation, DateTime expirationTime)
    {
        if (_disposed || !AllowChannelReuse || generation != _generation || FastDateTime.UtcNow >= expirationTime) {
            VhLogger.Instance.LogDebug(GeneralEventId.Stream,
                "A connection is closed rather than kept, as reuse is off, the connector is disposed, or the " +
                "connection is retired or past its lifespan. ConnectionId: {ConnectionId}", connection.ConnectionId);

            Close(connection);
            return;
        }

        // it goes when its idle time or its lifespan is up, whichever comes first
        var idleExpirationTime = FastDateTime.UtcNow + _idleConnectionTimeout;
        _idleConnectionItems.Enqueue(new IdleConnectionItem {
            StreamConnection = connection,
            Generation = generation,
            ExpirationTime = idleExpirationTime < expirationTime ? idleExpirationTime : expirationTime
        });

        // disposed meanwhile: its drain may have come before this enqueue, and nothing dequeues after it
        if (_disposed)
            RetireConnections();
    }

    // also one retired while it went back to the queue
    private bool IsExpired(IdleConnectionItem item)
    {
        return item.ExpirationTime <= FastDateTime.UtcNow || item.Generation != _generation ||
               !item.StreamConnection.Connected;
    }

    // The quick close: with reuse prevented, the graceful one would do no more, as a connection here has no
    // stream in progress to end with its peer
    private static void Close(ReusableStreamConnection connection)
    {
        connection.PreventReuse();
        connection.Dispose();
    }

    private ValueTask Cleanup(CancellationToken cancellationToken)
    {
        // Kill all expired client streams
        while (_idleConnectionItems.TryPeek(out var queueItem) && IsExpired(queueItem)) {
            if (_idleConnectionItems.TryDequeue(out queueItem))
                Close(queueItem.StreamConnection);
        }

        return ValueTask.CompletedTask;
    }

    // connections made before it are not reused: the idle ones close now, the ones in use when they come back
    private void RetireConnections()
    {
        Interlocked.Increment(ref _generation);
        while (_idleConnectionItems.TryDequeue(out var queueItem))
            Close(queueItem.StreamConnection);
    }

    // The host moved to other networks: every connection is from the old ones, so none goes back to the
    // pool, the idle ones close, and QUIC's pooled connections go. QUIC's go first: an idle QUIC stream's close
    // waits for its peer's acknowledgment, which on a dead network never comes until its connection is closed
    public void DropConnections()
    {
        _quicConnectionFactory?.DropConnections();
        RetireConnections();
    }

    public bool AllowChannelReuse {
        get;
        set {
            field = value;
            if (!value)
                RetireConnections();
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
        RetireConnections();
    }

    // async only: the QUIC factory's close waits for its connections' closes
    public async ValueTask DisposeAsync()
    {
        // a second close waits for the first one's end
        using var scopeLock = await _disposeLock.LockAsync().Vhc();
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        // Stop the cleanup job
        _cleanupJob.Dispose();

        // Dispose connection factories
        _tcpConnectionFactory.Dispose();
        if (_quicConnectionFactory != null) {
            _quicConnectionFactory.PanicRecycled -= QuicConnectionFactory_PanicRecycled;
            await _quicConnectionFactory.DisposeAsync().Vhc();
        }

        // the idle connections close now, the ones in use when they come back
        RetireConnections();
    }

    private class IdleConnectionItem
    {
        public required ReusableStreamConnection StreamConnection { get; init; }
        public required int Generation { get; init; }
        public required DateTime ExpirationTime { get; init; }
    }

}
