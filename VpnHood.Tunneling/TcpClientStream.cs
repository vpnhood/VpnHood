using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using VpnHood.Common.Net;

namespace VpnHood.Tunneling;

public class TcpClientStream : IDisposable
{
    private bool _disposed;

    public TcpClientStream(TcpClient tcpClient, Stream stream)
    {
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        TcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
    }

    public TcpClient TcpClient { get; }
    public Stream Stream { get; set; }
    public IPEndPoint LocalEndPoint => (IPEndPoint)TcpClient.Client.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => (IPEndPoint)TcpClient.Client.RemoteEndPoint;
    public IPEndPointPair IpEndPointPair => new (LocalEndPoint, RemoteEndPoint);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stream.Dispose();
        TcpClient.Dispose();
    }
}