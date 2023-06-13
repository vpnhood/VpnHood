using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Tunneling.Channels;

namespace VpnHood.Tunneling.ClientStreams;

public class TcpClientStream : IClientStream
{
    public delegate Task ReuseCallback(IClientStream clientStream);

    private static long _lastId;
    private bool _disposed;
    private readonly ReuseCallback? _reuseCallback;
    public string ClientStreamId { get; }

    public TcpClientStream(TcpClient tcpClient, Stream stream, ReuseCallback? reuseCallback = null)
    {
        Interlocked.Increment(ref _lastId);
        ClientStreamId = _lastId.ToString();
        _reuseCallback = reuseCallback;
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        TcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
        IpEndPointPair = new IPEndPointPair((IPEndPoint)TcpClient.Client.LocalEndPoint, (IPEndPoint)TcpClient.Client.RemoteEndPoint);
        VhLogger.Instance.LogTrace(GeneralEventId.TcpLife, "A TcpClientStream has been created. ClientStreamId: {ClientStreamId}", ClientStreamId);
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

    public async ValueTask DisposeAsync(bool allowReuse)
    {
        if (_disposed) return;
        _disposed = true;

        if (allowReuse && _reuseCallback != null && CheckIsAlive() && Stream is HttpStream httpStream)
        {
            try
            {
                VhLogger.Instance.LogTrace(GeneralEventId.TcpLife, "A TcpClientStream has been freed. ClientStreamId: {ClientStreamId}", ClientStreamId);
                _ = _reuseCallback?.Invoke(new TcpClientStream(TcpClient, await httpStream.CreateReuse(), _reuseCallback));
            }
            catch (Exception ex)
            {
                VhLogger.LogError(GeneralEventId.TcpLife, ex, "Could not reuse the TcpClientStream. ClientStreamId: {ClientStreamId}", ClientStreamId);
                await Stream.DisposeAsync();
                TcpClient.Dispose();
            }
            return;
        }

        // close without reuse
        VhLogger.Instance.LogTrace(GeneralEventId.TcpLife, "A TcpClientStream has been disposed. ClientStreamId: {ClientStreamId}", ClientStreamId);
        await Stream.DisposeAsync();
        TcpClient.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        return DisposeAsync(true);
    }
}