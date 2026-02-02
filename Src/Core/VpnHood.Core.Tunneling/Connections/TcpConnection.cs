using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Channels.Streams;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling.Connections;

public sealed class TcpConnection : IConnection
{
    private readonly TcpClient _tcpClient;

    public TcpConnection(TcpClient tcpClient,
        string connectionName, bool isServer, string? connectionId = null)
    {
        _tcpClient = tcpClient;
        ConnectionName = connectionName;
        IsServer = isServer;
        Stream = tcpClient.GetStream();
        ConnectionId = connectionId ?? UniqueIdFactory.Create();
    }

    public TcpConnection(TcpClient tcpClient, Stream stream,
        string connectionName, bool isServer, string? connectionId = null)
        : this(tcpClient, connectionName: connectionName, isServer: isServer, connectionId: connectionId)
    {
        Stream = stream;
    }

    public string ConnectionName { get; }
    public bool IsServer { get; }
    public bool Connected => VhUtils.IsTcpClientHealthy(_tcpClient);
    public Stream Stream { get; }
    public IPEndPoint LocalEndPoint => _tcpClient.GetLocalEndPoint();
    public IPEndPoint RemoteEndPoint => _tcpClient.GetRemoteEndPoint();
    public bool RequireHttpResponse { get; set; }

    public string ConnectionId {
        get;
        set {
            if (field != value)
                VhLogger.Instance.LogDebug(GeneralEventId.Stream,
                    "ConnectionId has been changed. ConnectionId: {ConnectionId}, NewConnectionId: {NewConnectionId}",
                    field, value);

            field = value;
            if (Stream is ChunkStream chunkStream)
                chunkStream.StreamId = value;
        }
    }

    public override string ToString()
    {
        var role = IsServer ? "server" : "client";
        return $"{ConnectionId}:{ConnectionName}:{role}";
    }

    public void Dispose()
    {
        Stream.Dispose();
        _tcpClient.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await Stream.DisposeAsync();
        _tcpClient.Dispose();
    }
}