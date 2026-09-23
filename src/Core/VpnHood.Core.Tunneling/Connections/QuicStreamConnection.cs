using Microsoft.Extensions.Logging;
using System.Net;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Memory;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling.Connections;

public sealed class QuicStreamConnection : IStreamConnection
{
    private readonly Stream _stream;
    private bool _disposed;
    public event EventHandler? Disposed;
    public string ConnectionName { get; }
    public bool IsServer { get; }
    public bool Connected { get; private set; } = true;
    public Stream Stream => _stream;
    public IPEndPoint LocalEndPoint { get; }

    public IPEndPoint RemoteEndPoint { get; }

    public bool RequireHttpResponse { get; set; }

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
        if (Interlocked.Exchange(ref _disposed, true)) return;
        Connected = false;
        _stream.Dispose();
        VhTypeTracker.Record("QuicStreamConnection.disposed");
        Disposed?.Invoke(this, EventArgs.Empty);

        VhLogger.Instance.LogTrace(GeneralEventId.Stream,
            "QuicStreamConnection has been disposed. ConnectionId: {ConnectionId}", ConnectionId);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, true)) return;
        Connected = false;
        await Stream.DisposeAsync();
        VhTypeTracker.Record("QuicStreamConnection.disposed");
        Disposed?.Invoke(this, EventArgs.Empty);

        VhLogger.Instance.LogTrace(GeneralEventId.Stream,
            "Connection has been disposed asynchronously. ConnectionId: {ConnectionId}",
            ConnectionId);
    }
}
