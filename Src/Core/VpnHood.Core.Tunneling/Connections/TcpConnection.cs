using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Channels.Streams;

namespace VpnHood.Core.Tunneling.Connections;

public sealed class TcpConnection(TcpClient tcpClient, string id, Stream? stream = null) 
    : IConnection
{
    public bool Connected => VhUtils.IsTcpClientHealthy(tcpClient);
    public Stream Stream { get; } = stream ?? tcpClient.GetStream();
    public IPEndPoint LocalEndPoint => tcpClient.GetLocalEndPoint();
    public IPEndPoint RemoteEndPoint => tcpClient.GetRemoteEndPoint();
    public bool RequireHttpResponse { get; set; }

    public string Id {
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
    } = id;


    public void Dispose()
    {
        Stream.Dispose();
        tcpClient.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await Stream.DisposeAsync();
        tcpClient.Dispose();
    }
}