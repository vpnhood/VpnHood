using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using VpnHood.Common.Net;

namespace VpnHood.Tunneling.ClientStreams;

public class TcpClientStream : IClientStream
{
    private bool _disposed;

    public TcpClientStream(TcpClient tcpClient, Stream stream)
    {
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        TcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
        IpEndPointPair = new IPEndPointPair((IPEndPoint)TcpClient.Client.LocalEndPoint, (IPEndPoint)TcpClient.Client.RemoteEndPoint);
    }

    public TcpClient TcpClient { get; }
    public Stream Stream { get; set; }
    public bool NoDelay { get => TcpClient.NoDelay; set => TcpClient.NoDelay = value; }
    public int ReceiveBufferSize { get => TcpClient.ReceiveBufferSize; set => TcpClient.ReceiveBufferSize = value; }
    public int SendBufferSize { get => TcpClient.SendBufferSize; set => TcpClient.SendBufferSize = value; }
    public IPEndPointPair IpEndPointPair { get; }
    public bool AllowReuse { get; set; }

    public bool CheckIsAlive()
    {
        try
        {
            return !TcpClient.Client.Poll(0, SelectMode.SelectError);
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

        await Stream.DisposeAsync();
        TcpClient.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        return DisposeAsync(true);
    }
}