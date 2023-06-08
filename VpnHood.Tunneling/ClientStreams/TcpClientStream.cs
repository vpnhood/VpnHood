using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;

namespace VpnHood.Tunneling.ClientStreams;

public class TcpClientStream : IClientStream
{
    public delegate Task ReuseCallback(IClientStream clientStream);

    private bool _disposed;
    private readonly ReuseCallback? _reuseCallback;

    public TcpClientStream(TcpClient tcpClient, Stream stream, ReuseCallback? reuseCallback = null)
    {
        _reuseCallback = reuseCallback;
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        TcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
        IpEndPointPair = new IPEndPointPair((IPEndPoint)TcpClient.Client.LocalEndPoint, (IPEndPoint)TcpClient.Client.RemoteEndPoint);
        VhLogger.Instance.LogTrace(GeneralEventId.Tcp, "A TcpClientStream has been created.");
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

        if (allowReuse && _reuseCallback != null && CheckIsAlive())
        {
            VhLogger.Instance.LogTrace(GeneralEventId.Tcp, $"A {VhLogger.FormatType(this)} has been freed.");
            _ = _reuseCallback?.Invoke(new TcpClientStream(TcpClient, Stream, _reuseCallback));
        }
        else
        {
            VhLogger.Instance.LogTrace(GeneralEventId.Tcp, $"A {VhLogger.FormatType(this)} has been disposed.");
            await Stream.DisposeAsync();
            TcpClient.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        return DisposeAsync(true);
    }
}