﻿using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using VpnHood.Common.Net;

namespace VpnHood.Tunneling.ClientStreams;

public class HttpClientStream : IClientStream
{
    private bool _disposed;


    public HttpClientStream(TcpClient tcpClient, Stream stream)
    {
        Stream = stream ?? throw new ArgumentNullException(nameof(stream));
        TcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
        IpEndPointPair = new IPEndPointPair((IPEndPoint)TcpClient.Client.LocalEndPoint, (IPEndPoint)TcpClient.Client.RemoteEndPoint);
    }

    private TcpClient TcpClient { get; }
    public Stream Stream { get; }
    public bool NoDelay { get => TcpClient.NoDelay; set => TcpClient.NoDelay = value; }
    public int ReceiveBufferSize { get => TcpClient.ReceiveBufferSize; set => TcpClient.ReceiveBufferSize = value; }
    public int SendBufferSize { get => TcpClient.SendBufferSize; set => TcpClient.SendBufferSize = value; }
    public IPEndPointPair IpEndPointPair { get; }

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stream.Dispose();
        TcpClient.Dispose();
    }
}