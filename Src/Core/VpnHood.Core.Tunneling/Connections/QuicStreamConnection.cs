using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Quic;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling.Connections;

public sealed class QuicStreamConnection : IStreamConnection
{
    private readonly QuicStream _stream;
    private bool _connected = true;
    private int _disposed;

    public QuicStreamConnection(QuicStream stream, IPEndPoint localEndPoint, IPEndPoint remoteEndPoint,
        string connectionName, bool isServer, string? connectionId = null)
    {
        _stream = stream;
        LocalEndPoint = localEndPoint;
        RemoteEndPoint = remoteEndPoint;
        ConnectionName = connectionName;
        IsServer = isServer;
        ConnectionId = connectionId ?? UniqueIdFactory.Create();
    }

    public event EventHandler? Disposed;
    public string ConnectionName { get; }
    public bool IsServer { get; }
    public bool Connected => _connected;
    public Stream Stream => _stream;
    public IPEndPoint LocalEndPoint { get; }

    public IPEndPoint RemoteEndPoint { get; }

    public bool RequireHttpResponse { get; set; }

    public string ConnectionId {
        get;
        set {
            if (field != value)
                VhLogger.Instance.LogDebug(GeneralEventId.Stream,
                    "QuicConnectionId has been changed. ConnectionId: {ConnectionId}, NewConnectionId: {NewConnectionId}",
                    field, value);
            field = value;
            if (Stream is ChunkStream chunkStream)
                chunkStream.StreamId = value;
        }
    }

    public override string ToString()
    {
        var role = IsServer ? "server" : "client";
        return $"{ConnectionId}:{ConnectionName}:{role}:quic";
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _connected = false;
        _stream.Dispose();
        Disposed?.Invoke(this, EventArgs.Empty);

        VhLogger.Instance.LogTrace(GeneralEventId.Stream,
            "QuicStreamConnection has been disposed. ConnectionId: {ConnectionId}", ConnectionId);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _connected = false;
        await Stream.DisposeAsync();
        Disposed?.Invoke(this, EventArgs.Empty);

        VhLogger.Instance.LogTrace(GeneralEventId.Stream,
            "Connection has been disposed asynchronously. ConnectionId: {ConnectionId}",
            ConnectionId);
    }
}
