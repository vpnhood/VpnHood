using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Tunneling.Channels.Streams;

namespace VpnHood.Tunneling.ClientStreams;

public class TcpClientStream : IClientStream
{
    private readonly ReuseCallback? _reuseCallback;
    private string _clientStreamId;

    public delegate Task ReuseCallback(IClientStream clientStream);
    public TcpClient TcpClient { get; }
    public Stream Stream { get; set; }
    public IPEndPointPair IpEndPointPair { get; }

    public string ClientStreamId
    {
        get => _clientStreamId;
        set
        {
            if (_clientStreamId != value)
                VhLogger.Instance.LogTrace(GeneralEventId.TcpLife,
                    "ClientStreamId has been changed. ClientStreamId: {ClientStreamId}, NewClientStreamId: {NewClientStreamId}",
                    _clientStreamId, value);

            _clientStreamId = value;
            if (Stream is ChunkStream chunkStream)
                chunkStream.StreamId = value;
        }
    }

    public TcpClientStream(TcpClient tcpClient, Stream stream, string clientStreamId, ReuseCallback? reuseCallback = null)
        : this(tcpClient, stream, clientStreamId, reuseCallback, true)
    {
    }

    private TcpClientStream(TcpClient tcpClient, Stream stream, string clientStreamId, ReuseCallback? reuseCallback, bool log)
    {
        _clientStreamId = clientStreamId;
        _reuseCallback = reuseCallback;
        Stream = stream;
        TcpClient = tcpClient;
        IpEndPointPair = new IPEndPointPair((IPEndPoint)TcpClient.Client.LocalEndPoint, (IPEndPoint)TcpClient.Client.RemoteEndPoint);

        if (log)
            VhLogger.Instance.LogTrace(GeneralEventId.TcpLife,
                "A TcpClientStream has been created. ClientStreamId: {ClientStreamId}, StreamType: {StreamType}, LocalEp: {LocalEp}, RemoteEp: {RemoteEp}",
                ClientStreamId, stream.GetType().Name, VhLogger.Format(IpEndPointPair.LocalEndPoint), VhLogger.Format(IpEndPointPair.RemoteEndPoint));
    }

    public bool CheckIsAlive()
    {
        return VhUtil.IsTcpClientHealthy(TcpClient);
    }

    public ValueTask DisposeAsync()
    {
        return DisposeAsync(true);
    }

    private readonly AsyncLock _disposeLock = new();
    public bool Disposed { get; private set; }
    public async ValueTask DisposeAsync(bool graceful)
    {
        using var lockResult = await _disposeLock.LockAsync();
        if (Disposed) return;
        Disposed = true;

        var chunkStream = Stream as ChunkStream;
        if (graceful && _reuseCallback != null && CheckIsAlive() && chunkStream?.CanReuse == true)
        {
            Stream? newStream = null;
            try
            {
                newStream = await chunkStream.CreateReuse();
                _ = _reuseCallback.Invoke(new TcpClientStream(TcpClient, newStream, ClientStreamId, _reuseCallback, false));

                VhLogger.Instance.LogTrace(GeneralEventId.TcpLife,
                    "A TcpClientStream has been freed. ClientStreamId: {ClientStreamId}", ClientStreamId);
            }
            catch (Exception ex)
            {
                VhLogger.LogError(GeneralEventId.TcpLife, ex,
                    "Could not reuse the TcpClientStream. ClientStreamId: {ClientStreamId}", ClientStreamId);

                if (newStream != null) await newStream.DisposeAsync();
                await Stream.DisposeAsync();
                TcpClient.Dispose();
            }
        }
        else
        {
            // close streams
            await Stream.DisposeAsync(); // first close stream 2
            TcpClient.Dispose();

            VhLogger.Instance.LogTrace(GeneralEventId.TcpLife,
                "A TcpClientStream has been disposed. ClientStreamId: {ClientStreamId}",
                ClientStreamId);
        }
    }
}