using Microsoft.Extensions.Logging;
using System.Net;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Memory;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling.Connections;

/// <summary>Represents a QUIC stream as a stream connection.</summary>
public sealed class QuicStreamConnection : IStreamConnection
{
    private readonly Stream _stream;
    private readonly AsyncLock _disposeLock = new();
    private bool _disposed;

    /// <summary>
    /// Occurs once the connection has closed, its stream's close failed or not, as its QUIC connection counts its
    /// streams down on it.
    /// </summary>
    public event EventHandler? Disposed;

    /// <inheritdoc/>
    public string ConnectionName { get; }

    /// <inheritdoc/>
    public bool IsServer { get; }

    /// <inheritdoc/>
    /// <remarks>It is <see langword="true"/> until the connection's close.</remarks>
    public bool Connected { get; private set; } = true;

    /// <inheritdoc/>
    public Stream Stream => _stream;

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

    /// <summary>Initializes a new instance of the <see cref="QuicStreamConnection"/> class.</summary>
    /// <param name="stream">The QUIC stream, which the connection owns.</param>
    /// <param name="localEndPoint">The QUIC connection's local endpoint.</param>
    /// <param name="remoteEndPoint">The QUIC connection's remote endpoint.</param>
    /// <param name="connectionName">The connection's name, which its logs carry.</param>
    /// <param name="isServer"><see langword="true"/> for the server's side of the connection.</param>
    /// <param name="connectionId">The connection's ID, or <see langword="null"/> for a new one.</param>
    public QuicStreamConnection(Stream stream, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint,
        string connectionName, bool isServer, string? connectionId = null)
    {
        _stream = stream;
        LocalEndPoint = localEndPoint;
        RemoteEndPoint = remoteEndPoint;
        ConnectionName = connectionName;
        IsServer = isServer;
        ConnectionId = connectionId ?? UniqueIdFactory.Create();
        VhTypeTracker.Track(this);
    }

    /// <inheritdoc/>
    public string ConnectionId {
        get;
        set {
            if (field != value)
                VhLogger.Instance.LogDebug(GeneralEventId.Stream,
                    "QuicConnectionId has been changed. ConnectionId: {ConnectionId}, NewConnectionId: {NewConnectionId}",
                    field, value);
            field = value;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It is the ID, the name, the side and the transport: <c>id:name:server:quic</c> or <c>id:name:client:quic</c>.
    /// </remarks>
    public override string ToString()
    {
        var role = IsServer ? "server" : "client";
        return $"{ConnectionId}:{ConnectionName}:{role}:quic";
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It starts <see cref="DisposeAsync"/> and does not wait for it: a QUIC stream's synchronous close waits for its
    /// peer, on a dead network until its connection gives up.
    /// </remarks>
    public void Dispose()
    {
        _ = this.TryDisposeAsync();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It closes the stream, then raises <see cref="Disposed"/>; a second call waits for the first one's end.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        // a second close waits for the first one's end
        using var scopeLock = await _disposeLock.LockAsync(CancellationToken.None).Vhc();
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        Connected = false;

        // a failed close still reports the connection closed, as its QUIC connection counts its streams down on that
        await Stream.TryDisposeAsync().Vhc();
        VhTypeTracker.Record("QuicStreamConnection.disposed");
        Disposed?.Invoke(this, EventArgs.Empty);

        VhLogger.Instance.LogTrace(GeneralEventId.Stream,
            "Connection has been disposed asynchronously. ConnectionId: {ConnectionId}",
            ConnectionId);
    }
}
