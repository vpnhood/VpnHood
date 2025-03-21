using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Channels.Streams;

namespace VpnHood.Core.Tunneling.ClientStreams;

public class TcpClientStream : IClientStream
{
    private readonly ReuseCallback? _reuseCallback;
    private string _clientStreamId;

    public delegate Task ReuseCallback(IClientStream clientStream);

    public TcpClient TcpClient { get; }
    public Stream Stream { get; set; }
    public bool RequireHttpResponse { get; set; }
    public IPEndPointPair IpEndPointPair { get; }

    public string ClientStreamId {
        get => _clientStreamId;
        set {
            if (_clientStreamId != value)
                VhLogger.Instance.LogDebug(GeneralEventId.TcpLife,
                    "ClientStreamId has been changed. ClientStreamId: {ClientStreamId}, NewClientStreamId: {NewClientStreamId}",
                    _clientStreamId, value);

            _clientStreamId = value;
            if (Stream is ChunkStream chunkStream)
                chunkStream.StreamId = value;
        }
    }

    public TcpClientStream(TcpClient tcpClient, Stream stream, string clientStreamId,
        ReuseCallback? reuseCallback = null)
        : this(tcpClient, stream, clientStreamId, reuseCallback, true)
    {
    }

    private TcpClientStream(TcpClient tcpClient, Stream stream, string clientStreamId, ReuseCallback? reuseCallback,
        bool log)
    {
        _clientStreamId = clientStreamId;
        _reuseCallback = reuseCallback;
        Stream = stream;
        TcpClient = tcpClient;
        IpEndPointPair = new IPEndPointPair((IPEndPoint)TcpClient.Client.LocalEndPoint,
            (IPEndPoint)TcpClient.Client.RemoteEndPoint);

        if (log)
            VhLogger.Instance.LogDebug(GeneralEventId.TcpLife,
                "A TcpClientStream has been created. ClientStreamId: {ClientStreamId}, StreamType: {StreamType}, LocalEp: {LocalEp}, RemoteEp: {RemoteEp}",
                ClientStreamId, stream.GetType().Name,
                VhLogger.Format(IpEndPointPair.LocalEndPoint), VhLogger.Format(IpEndPointPair.RemoteEndPoint));
    }

    public bool CheckIsAlive()
    {
        return VhUtils.IsTcpClientHealthy(TcpClient);
    }

    public ValueTask DisposeAsync()
    {
        return DisposeAsync(true);
    }

    private readonly AsyncLock _disposeLock = new();
    public bool Disposed { get; private set; }

    public async ValueTask DisposeAsync(bool graceful)
    {
        using var lockResult = await _disposeLock.LockAsync().VhConfigureAwait();
        if (Disposed) return;
        Disposed = true;
        var chunkStream = Stream as ChunkStream;

        // dispose the stream if we can't reuse it
        if (!graceful || _reuseCallback == null || !CheckIsAlive() || chunkStream?.CanReuse != true) {
            // close streams
            await Stream.DisposeAsync().VhConfigureAwait(); // first close stream 2
            TcpClient.Dispose();

            VhLogger.Instance.LogDebug(GeneralEventId.TcpLife,
                "A TcpClientStream has been disposed. ClientStreamId: {ClientStreamId}",
                ClientStreamId);
            return;
        }

        // reuse the stream
        try {
            await Reuse(chunkStream, TcpClient, _reuseCallback, ClientStreamId).VhConfigureAwait();
        }
        catch (Exception ex) {
            VhLogger.LogError(GeneralEventId.TcpLife, ex,
                "Could not reuse the TcpClientStream. ClientStreamId: {ClientStreamId}", ClientStreamId);

            await Stream.DisposeAsync().VhConfigureAwait();
            TcpClient.Dispose();
        }

    }

    private async Task Reuse(ChunkStream chunkStream, TcpClient tcpClient, 
        ReuseCallback reuseCallback, string clientStreamId)
    {
        VhLogger.Instance.LogDebug(GeneralEventId.TcpLife,
            "Reusing a TcpClientStream. ClientStreamId: {ClientStreamId}", ClientStreamId);

        var newStream = await chunkStream.CreateReuse().VhConfigureAwait();

        try {
            // we don't call SuppressFlow on debug to allow MsTest shows console output in nested task
            using IDisposable? suppressFlow = IsDebug ? null : ExecutionContext.SuppressFlow();
            _ = Task.Run(() => reuseCallback.Invoke(new TcpClientStream(tcpClient, newStream, clientStreamId, reuseCallback, false)));
        }
        catch (Exception) {
            await newStream.DisposeAsync().VhConfigureAwait();
            throw;
        }
    }

#if DEBUG
    private static bool IsDebug => true;
#else
    private static bool IsDebug => false;
#endif
}