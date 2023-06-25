using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Tunneling.Channels;

namespace VpnHood.Tunneling.ClientStreams;

public class TcpClientStream : IClientStream
{
    public delegate Task ReuseCallback(IClientStream clientStream);

    private bool _disposed;
    private readonly ReuseCallback? _reuseCallback;
    private string _clientStreamId;

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
            if (Stream is HttpStream httpStream)
                httpStream.StreamId = value;
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
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        TcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
        IpEndPointPair = new IPEndPointPair((IPEndPoint)TcpClient.Client.LocalEndPoint, (IPEndPoint)TcpClient.Client.RemoteEndPoint);

        if (log)
            VhLogger.Instance.LogTrace(GeneralEventId.TcpLife,
                "A TcpClientStream has been created. ClientStreamId: {ClientStreamId}, LocalEp: {LocalEp}, RemoteEp: {RemoteEp}",
                ClientStreamId, VhLogger.Format(IpEndPointPair.LocalEndPoint), VhLogger.Format(IpEndPointPair.RemoteEndPoint));
    }

    public TcpClient TcpClient { get; }
    public Stream Stream { get; set; }
    public IPEndPointPair IpEndPointPair { get; }

    public bool CheckIsAlive()
    {
        try
        {
            return TcpClient.Connected && !TcpClient.Client.Poll(0, SelectMode.SelectError);
        }
        catch
        {
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        return DisposeAsync(true);
    }

    private readonly AsyncLock _disposeLock = new();
    private ValueTask? _disposeTask;
    public async ValueTask DisposeAsync(bool allowReuse, bool graceful = true)
    {
        if (!graceful)
            TcpClient.Dispose();

        lock (_disposeLock)
            _disposeTask ??= DisposeAsyncCore(allowReuse);
        await _disposeTask.Value;
    }

    private async ValueTask DisposeAsyncCore(bool allowReuse)
    {
        if (_disposed) return;
        _disposed = true;

        var httpStream = Stream as HttpStream;
        if (allowReuse && _reuseCallback != null && CheckIsAlive() && httpStream?.CanReuse == true)
        {
            Stream? newStream = null;
            try
            {
                newStream = await httpStream.CreateReuse();
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
            return;
        }

        // close the stream
        await Stream.DisposeAsync();
        TcpClient.Dispose();

        VhLogger.Instance.LogTrace(GeneralEventId.TcpLife,
            "A TcpClientStream has been disposed. ClientStreamId: {ClientStreamId}",
            ClientStreamId);
    }
}