using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using VpnHood.Logging;

namespace VpnHood.Tunneling
{
    public class TcpClientStream : IDisposable
    {
        private bool _disposed = false;
        public TcpClient TcpClient { get; }
        public Stream Stream { get; set; }
        public IPEndPoint LocalEndPoint => (IPEndPoint)TcpClient.Client.LocalEndPoint;
        public IPEndPoint RemoteEndPoint => (IPEndPoint)TcpClient.Client.RemoteEndPoint;

        public TcpClientStream(TcpClient tcpClient, Stream stream)
        {
            Stream = stream ?? throw new ArgumentNullException(nameof(stream));
            TcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Stream.Dispose();
                TcpClient.Dispose();

                _disposed = true;
            }
        }
    }
}
