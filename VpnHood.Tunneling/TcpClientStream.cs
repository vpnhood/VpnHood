using Microsoft.Extensions.Logging;
using System;
using System.IO;
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

        public TcpClientStream(TcpClient tcpClient, Stream stream)
        {
            Stream = stream ?? throw new ArgumentNullException(nameof(stream));
            TcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stream.Dispose();
            TcpClient.Dispose();
        }
    }
}
