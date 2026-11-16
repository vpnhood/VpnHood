using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Memory;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling.Connections;

/// <summary>Represents a TCP connection as a stream connection.</summary>
public sealed class TcpStreamConnection : IStreamConnection
{
    private readonly TcpClient _tcpClient;
    private readonly Socket _socket;
    private readonly AsyncLock _disposeLock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpStreamConnection"/> class, on the TCP client's own stream.
    /// </summary>
    /// <param name="tcpClient">The connected TCP client, which the connection owns.</param>
    /// <param name="connectionName">The connection's name, which its logs carry.</param>
    /// <param name="isServer"><see langword="true"/> for the server's side of the connection.</param>
    /// <param name="connectionId">The connection's ID, or <see langword="null"/> for a new one.</param>
    /// <exception cref="ObjectDisposedException">The TCP client has no socket: it is closed.</exception>
    /// <exception cref="InvalidOperationException">The TCP client's socket has no local or remote endpoint.</exception>
    public TcpStreamConnection(TcpClient tcpClient,
        string connectionName, bool isServer, string? connectionId = null)
    {
        _tcpClient = tcpClient;
        // The cap can dispose the client and clear Client while this connection is still finishing a request.
        _socket = tcpClient.Client ?? throw new ObjectDisposedException(nameof(tcpClient));
        LocalEndPoint = _socket.GetLocalEndPoint();
        RemoteEndPoint = _socket.GetRemoteEndPoint();
        ConnectionName = connectionName;
        IsServer = isServer;
        Stream = tcpClient.GetStream();
        ConnectionId = connectionId ?? UniqueIdFactory.Create();
        VhTypeTracker.Track(this, $"TcpStreamConnection.{connectionName}");
        VhTypeTracker.Track(tcpClient, $"TcpClient.{connectionName}");
        VhTypeTracker.Track(Stream, $"NetworkStream.{connectionName}");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TcpStreamConnection"/> class, on a stream over the TCP client's.
    /// </summary>
    /// <param name="tcpClient">The connected TCP client, which the connection owns.</param>
    /// <param name="stream">The stream to read and write through, over the client's, which the connection owns.</param>
    /// <param name="connectionName">The connection's name, which its logs carry.</param>
    /// <param name="isServer"><see langword="true"/> for the server's side of the connection.</param>
    /// <param name="connectionId">The connection's ID, or <see langword="null"/> for a new one.</param>
    /// <exception cref="ObjectDisposedException">The TCP client has no socket: it is closed.</exception>
    /// <exception cref="InvalidOperationException">The TCP client's socket has no local or remote endpoint.</exception>
    public TcpStreamConnection(TcpClient tcpClient, Stream stream,
        string connectionName, bool isServer, string? connectionId = null)
        : this(tcpClient, connectionName: connectionName, isServer: isServer, connectionId: connectionId)
    {
        Stream = stream;
        VhTypeTracker.Track(stream, $"{stream.GetType().Name}.{connectionName}");
    }

    /// <inheritdoc/>
    public string ConnectionName { get; }

    /// <inheritdoc/>
    public bool IsServer { get; }

    /// <inheritdoc/>
    /// <remarks>It asks the TCP client's socket.</remarks>
    public bool Connected => VhUtils.IsTcpClientHealthy(_tcpClient);

    /// <inheritdoc/>
    public Stream Stream { get; }

    /// <inheritdoc/>
    public IPEndPoint LocalEndPoint { get; }

    /// <inheritdoc/>
    public IPEndPoint RemoteEndPoint { get; }

    /// <inheritdoc/>
    public bool RequireHttpResponse { get; set; }

    /// <inheritdoc/>
    /// <remarks>It does nothing: reuse is the reusable connection's, over this one.</remarks>
    public void PreventReuse()
    {
    }

    /// <inheritdoc/>
    public string ConnectionId {
        get;
        set {
            if (field != value)
                VhLogger.Instance.LogDebug(GeneralEventId.Stream,
                    "ConnectionId has been changed. ConnectionId: {ConnectionId}, NewConnectionId: {NewConnectionId}",
                    field, value);

            field = value;
        }
    }

    /// <inheritdoc/>
    /// <remarks>It is the ID, the name and the side: <c>id:name:server</c> or <c>id:name:client</c>.</remarks>
    public override string ToString()
    {
        var role = IsServer ? "server" : "client";
        return $"{ConnectionId}:{ConnectionName}:{role}";
    }

    /// <summary>
    /// Reads off what has arrived, before a close: up to 1 MB, for up to a tenth of a second, until the connection is
    /// quiet for 10 milliseconds or its peer has closed it.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the drain ends.</returns>
    /// <remarks>
    /// A close with unread data can discard a reply already written, so it reads what has arrived, as a web server's
    /// input pipe does, bounded in bytes and time so a sender cannot prolong the close.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task DrainArrived(CancellationToken cancellationToken)
    {
        using var totalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        totalCts.CancelAfter(TimeSpan.FromMilliseconds(100));
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(totalCts.Token);
        try {
            var buffer = new byte[4096];
            for (var total = 0; total < 1024 * 1024;) {
                idleCts.CancelAfter(TimeSpan.FromMilliseconds(10));
                var read = await _socket.ReceiveAsync(buffer.AsMemory(0, Math.Min(buffer.Length, 1024 * 1024 - total)),
                    SocketFlags.None, idleCts.Token).Vhc();
                if (read == 0)
                    break;
                total += read;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            // A quiet socket or the overall close deadline ends the drain without holding a pool thread.
        }
        catch (ObjectDisposedException) {
            // The cap may close the socket during this drain.
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionAborted or
                                            SocketError.ConnectionReset or SocketError.OperationAborted or
                                            SocketError.NotConnected or SocketError.Shutdown or SocketError.NotSocket) {
            // The peer has already ended the connection.
        }
    }

    /// <summary>
    /// Closes the connection with a reset rather than a FIN, as a server does that gives up on its peer.
    /// </summary>
    // The socket closes on its own first: the client's and the stream's close would shut it down, which sends a FIN
    // whatever its linger
    public void Reset()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        try {
            _socket.LingerState = new LingerOption(true, 0);
        }
        catch (ObjectDisposedException) {
            // Eviction may already have disposed the TcpClient and cleared its Client property.
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.OperationAborted or SocketError.NotSocket or
                                            SocketError.NotConnected) {
            // Eviction can also close the native handle while linger is being set.
        }
        finally {
            _socket.Dispose();
            DisposeStream();
        }
    }

    private void DisposeStream()
    {
        try {
            Stream.Dispose();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not close the connection stream. ConnectionId: {ConnectionId}",
                ConnectionId);
        }
        finally {
            _tcpClient.Dispose();
            VhTypeTracker.Record($"TcpStreamConnection.{ConnectionName}.disposed");
        }
    }

    /// <inheritdoc/>
    /// <remarks>It closes the stream and the TCP client at once.</remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        DisposeStream();

        VhLogger.Instance.LogTrace(GeneralEventId.Stream,
            "Connection has been disposed. ConnectionId: {ConnectionId}",
            ConnectionId);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It closes the stream gracefully, then the TCP client, which a failure of the stream's close skips; a second call
    /// waits for the first one's end.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        // a second close waits for the first one's end. One after a quick close returns: that close leaves nothing
        // running. A quick close that comes meanwhile closes the socket, which ends the stream's close below
        using var scopeLock = await _disposeLock.LockAsync(CancellationToken.None).Vhc();
        if (_disposed)
            return;

        await Stream.DisposeAsync().Vhc();
        Dispose();
    }
}
