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

public sealed class TcpStreamConnection : IStreamConnection
{
    private readonly TcpClient _tcpClient;
    private readonly AsyncLock _disposeLock = new();
    private bool _disposed;

    public TcpStreamConnection(TcpClient tcpClient,
        string connectionName, bool isServer, string? connectionId = null)
    {
        _tcpClient = tcpClient;
        ConnectionName = connectionName;
        IsServer = isServer;
        Stream = tcpClient.GetStream();
        ConnectionId = connectionId ?? UniqueIdFactory.Create();
        VhTypeTracker.Track(this, $"TcpStreamConnection.{connectionName}");
        VhTypeTracker.Track(tcpClient, $"TcpClient.{connectionName}");
        VhTypeTracker.Track(Stream, $"NetworkStream.{connectionName}");
    }

    public TcpStreamConnection(TcpClient tcpClient, Stream stream,
        string connectionName, bool isServer, string? connectionId = null)
        : this(tcpClient, connectionName: connectionName, isServer: isServer, connectionId: connectionId)
    {
        Stream = stream;
        VhTypeTracker.Track(stream, $"{stream.GetType().Name}.{connectionName}");
    }

    public string ConnectionName { get; }
    public bool IsServer { get; }
    public bool Connected => VhUtils.IsTcpClientHealthy(_tcpClient);
    public Stream Stream { get; }
    public IPEndPoint LocalEndPoint => _tcpClient.GetLocalEndPoint();
    public IPEndPoint RemoteEndPoint => _tcpClient.GetRemoteEndPoint();
    public bool RequireHttpResponse { get; set; }

    // reuse lives in the ReusableStreamConnection above
    public void PreventReuse()
    {
    }

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

    public override string ToString()
    {
        var role = IsServer ? "server" : "client";
        return $"{ConnectionId}:{ConnectionName}:{role}";
    }

    // a close with a reset rather than a FIN, as a server closes that gives up on its peer. The socket closes on its
    // own first: the client's and the stream's close would shut it down, which sends a FIN whatever its linger
    public void Reset()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        _tcpClient.Client.LingerState = new LingerOption(true, 0);
        _tcpClient.Client.Dispose();
        Stream.Dispose();
        _tcpClient.Dispose();
        VhTypeTracker.Record($"TcpStreamConnection.{ConnectionName}.disposed");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        Stream.Dispose();
        _tcpClient.Dispose();
        VhTypeTracker.Record($"TcpStreamConnection.{ConnectionName}.disposed");

        VhLogger.Instance.LogTrace(GeneralEventId.Stream,
            "Connection has been disposed. ConnectionId: {ConnectionId}",
            ConnectionId);
    }

    public async ValueTask DisposeAsync()
    {
        // a second close waits for the first one's end. One after a quick close returns: that close leaves nothing
        // running. A quick close that comes meanwhile closes the socket, which ends the stream's close below
        using var scopeLock = await _disposeLock.LockAsync().Vhc();
        if (_disposed)
            return;

        await Stream.DisposeAsync().Vhc();
        Dispose();
    }
}
