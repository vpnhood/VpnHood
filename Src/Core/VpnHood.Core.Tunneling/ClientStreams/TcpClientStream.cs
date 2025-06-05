using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Channels.Streams;

namespace VpnHood.Core.Tunneling.ClientStreams;

public class TcpClientStream : IClientStream
{
    private bool _disposed;
    private readonly object _reuseLock = new();
    private readonly ReuseCallback? _reuseCallback;
    private string _clientStreamId;
    private readonly TcpClient _tcpClient;
    private bool _allowReuse = true;
    private bool _reusing;

    public delegate void ReuseCallback(IClientStream clientStream);
    public Stream Stream { get; }
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

    public TcpClientStream(TcpClient tcpClient, Stream stream, string clientStreamId, ReuseCallback? reuseCallback = null)
    {
        _clientStreamId = clientStreamId;
        _reuseCallback = reuseCallback;
        Stream = stream;
        _tcpClient = tcpClient;
        IpEndPointPair = new IPEndPointPair(
            _tcpClient.Client.GetLocalEndPoint(), _tcpClient.Client.GetRemoteEndPoint());

        VhLogger.Instance.LogDebug(GeneralEventId.TcpLife,
            "A TcpClientStream has been created. ClientStreamId: {ClientStreamId}, StreamType: {StreamType}, LocalEp: {LocalEp}, RemoteEp: {RemoteEp}",
            ClientStreamId, stream.GetType().Name, VhLogger.Format(IpEndPointPair.LocalEndPoint), VhLogger.Format(IpEndPointPair.RemoteEndPoint));
    }

    public bool Connected {
        get {
            lock (_reuseLock)
                return !_disposed && VhUtils.IsTcpClientHealthy(_tcpClient);
        }
    }

    private bool CanReuse => _reuseCallback != null && Stream is ChunkStream { CanReuse: true };

    public void PreventReuse()
    {
        _allowReuse = false;
        if (Stream is ChunkStream chunkStream)
            chunkStream.PreventReuse();
    }

    private async Task ReuseThenDispose()
    {
        try {
            _reusing = true;
            VhLogger.Instance.LogDebug(GeneralEventId.TcpLife,
                "Reusing a TcpClientStream. ClientStreamId: {ClientStreamId}", ClientStreamId);

            // verify if we can reuse the stream
            if (_reuseCallback == null) throw new InvalidOperationException("Can not reuse the stream when reuseCallback is null.");
            if (Stream is not ChunkStream chunkStream) throw new InvalidOperationException("Can not reuse the stream when stream is not ChunkStream.");

            await Reuse(chunkStream, _reuseCallback).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(GeneralEventId.TcpLife, ex,
                "Could not reuse the TcpClientStream. ClientStreamId: {ClientStreamId}", ClientStreamId);

            // dispose and we should not try to reuse the stream
            PreventReuse();
            Dispose();
        }
        finally {
            _reusing = false;
        }
    }

    private async Task Reuse(ChunkStream chunkStream, ReuseCallback reuseCallback)
    {
        var newStream = await chunkStream.CreateReuse().Vhc();
        lock (_reuseLock) {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);

            // we don't call SuppressFlow on debug to allow MsTest shows console output in nested task
            using IDisposable? suppressFlow = IsDebug ? null : ExecutionContext.SuppressFlow(); 
            var newTcpClientStream = new TcpClientStream(_tcpClient, newStream, _clientStreamId, reuseCallback);
            Task.Run(() => reuseCallback(newTcpClientStream));

            _disposed = true;
        }
    }

    public void Dispose()
    {
        // prevent dispose when reuse is almost done. the owner is going to change soon 
        lock (_reuseLock) {
            if (_disposed)
                return;

            // If reuse is allowed, and we're not currently reusing, start reuse.
            if (_allowReuse) {
                // check if we are in the process of reusing the stream
                if (_reusing)
                    return;

                // reuse the stream in background
                // it will set _disposed to true 
                if (CanReuse) {
                    _ = ReuseThenDispose();
                    return;
                }
            }

            // prevent future reuse and stream reuse
            PreventReuse();

            // close stream and let cancel reuse if it is in progress
            Stream.Dispose();
            _tcpClient.Dispose();
            _disposed = true;
        }
    }

#if DEBUG
    private static bool IsDebug => true;
#else
    private static bool IsDebug => false;
#endif
}