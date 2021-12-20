using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using VpnHood.Common.Logging;

namespace VpnHood.Tunneling
{
    public class TcpClientStream : IDisposable
    {
        private bool _disposed;

        public static int c = 0; //todo
        public TcpClientStream(TcpClient tcpClient, Stream stream)
        {
            Stream = stream ?? throw new ArgumentNullException(nameof(stream));
            TcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
            Interlocked.Increment(ref c);
            VhLogger.Instance.LogWarning($"TcpClientStream: {c}");
        }

        public TcpClient TcpClient { get; }
        public Stream Stream { get; set; }
        public IPEndPoint LocalEndPoint => (IPEndPoint)TcpClient.Client.LocalEndPoint;
        public IPEndPoint RemoteEndPoint => (IPEndPoint)TcpClient.Client.RemoteEndPoint;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stream.Dispose();
            TcpClient.Dispose();
            Interlocked.Decrement(ref c);
            VhLogger.Instance.LogWarning($"TcpClientStream: {c}");
        }
    }
}