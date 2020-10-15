using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace VpnHood
{
    public class TcpClientStream : IDisposable
    {
        public TcpClient TcpClient { get; }
        public Stream Stream { get; set; }

        public TcpClientStream(TcpClient tcpClient, Stream stream)
        {
            Stream = stream ?? throw new ArgumentNullException(nameof(stream));
            TcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
        }

        private bool _disposed = false;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stream.Dispose();
            TcpClient.Close();
            TcpClient.Dispose();
        }
    }
}
