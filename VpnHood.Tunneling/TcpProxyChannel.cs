using System;
using System.IO;
using System.Threading;

namespace VpnHood.Tunneling
{
    public class TcpProxyChannel : IChannel
    {
        private TcpClientStream _orgTcpClientStream;
        private TcpClientStream _tunnelTcpClientStream;

        public event EventHandler OnFinished;
        public bool Connected { get; private set; }
        public long SentByteCount { get; private set; }
        public long ReceivedByteCount { get; private set; }

        public TcpProxyChannel(TcpClientStream orgTcpClientStream, TcpClientStream tunnelTcpClientStream)
        {
            _orgTcpClientStream = orgTcpClientStream ?? throw new ArgumentNullException(nameof(orgTcpClientStream));
            _tunnelTcpClientStream = tunnelTcpClientStream ?? throw new ArgumentNullException(nameof(tunnelTcpClientStream));
        }

        public void Start()
        {
            Connected = true;
            new Thread(TunnelReadingThread, TunnelUtil.SocketStackSize_Stream).Start(); //StackSize must be optimized!
            new Thread(TunnelWritingThread, TunnelUtil.SocketStackSize_Stream).Start(); //StackSize must be optimized!
        }

        private void TunnelReadingThread()
        {
            CopyTo(_tunnelTcpClientStream.Stream, _orgTcpClientStream.Stream, false);
            OnThreadEnd();
        }

        private void TunnelWritingThread()
        {
            CopyTo(_orgTcpClientStream.Stream, _tunnelTcpClientStream.Stream, true);
            OnThreadEnd();
        }

        private readonly object _lockCleanup = new object();
        private void OnThreadEnd()
        {
            lock (_lockCleanup)
            {
                Dispose();
                if (Connected)
                {
                    Connected = false;
                    OnFinished?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        // return total copied bytes
        private int CopyTo(Stream soruce, Stream destination, bool isSendingOut)
        {
            try
            {
                return CopyToInternal(soruce, destination, isSendingOut);
            }
            catch
            {
                return -2;
            }
        }

        // return total copied bytes
        private int CopyToInternal(Stream source, Stream destination, bool isSendingOut, long maxBytes = -1)
        {
            if (maxBytes == -1) maxBytes = long.MaxValue;

            //var isTunnelRead = source == _tunnelTcpClientStream.Stream || source == _tunnelTcpClientStream.TcpClient.GetStream();

            // Microsoft Stream Source Code:
            // We pick a value that is the largest multiple of 4096 that is still smaller than the large object heap threshold (85K).
            // The CopyTo/CopyToAsync buffer is short-lived and is likely to be collected at Gen0, and it offers a significant
            // improvement in Copy performance.
            const int bufferSize = 81920; // recommended by microsoft for copying buffers
            var buffer = new byte[bufferSize];
            int totalRead = 0;
            int bytesRead;
            while (true)
            {
                var count = (int)Math.Min(buffer.Length, maxBytes - totalRead);
                if (count == 0) break; // maxBytes has been reached
                bytesRead = source.Read(buffer, 0, count);
                if (bytesRead == 0) break; // end of the stream

                totalRead += bytesRead;

                if (!isSendingOut)
                    ReceivedByteCount += bytesRead;

                destination.Write(buffer, 0, bytesRead);

                if (isSendingOut)
                    SentByteCount += bytesRead;
            }

            return totalRead;
        }

        private bool _disposed = false;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _orgTcpClientStream.Dispose();
            _tunnelTcpClientStream.Dispose();
            _orgTcpClientStream = null;
            _tunnelTcpClientStream = null;
        }
    }
}
