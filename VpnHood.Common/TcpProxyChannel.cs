using System;
using System.IO;
using System.Threading;

namespace VpnHood
{
    public class TcpProxyChannel : IChannel
    {
        private readonly TcpClientStream _orgTcpClientStream;
        private readonly TcpClientStream _tunnelTcpClientStream;
        private readonly long _tlsLength;

        public event EventHandler OnFinished;

        // SentByteCount
        private readonly object _lockObject = new object();
        private bool _connected;
        public bool Connected
        {
            get { lock (_lockObject) return _connected; }
            set { lock (_lockObject) _connected = value; }
        }

        // SentByteCount
        private long _sentByteCount;
        public long SentByteCount
        {
            get => Interlocked.Read(ref _sentByteCount);
            set => Interlocked.Exchange(ref _sentByteCount, value);
        }

        // ReceivedByteCount
        public long _receivedByteCount;
        public long ReceivedByteCount
        {
            get => Interlocked.Read(ref _receivedByteCount);
            set => Interlocked.Exchange(ref _receivedByteCount, value);
        }

        //todo: remove tlsLength
        public TcpProxyChannel(TcpClientStream orgTcpClientStream, TcpClientStream tunnelTcpClientStream, long tlsLength)
        {
            _orgTcpClientStream = orgTcpClientStream ?? throw new ArgumentNullException(nameof(orgTcpClientStream));
            _tunnelTcpClientStream = tunnelTcpClientStream ?? throw new ArgumentNullException(nameof(tunnelTcpClientStream));
            _tlsLength = tlsLength;
        }

        public void Start()
        {
            Connected = true;
            new Thread(TunnelReadingThread, Util.SocketStackSize_Stream).Start(); //StackSize must be optimized!
            new Thread(TunnelWritingThread, Util.SocketStackSize_Stream).Start(); //StackSize must be optimized!
        }

        private void TunnelReadingThread()
        {
            var read = CopyTo(_tunnelTcpClientStream.Stream, _orgTcpClientStream.Stream, false, _tlsLength);
            if (read == _tlsLength)
            {
                CopyTo(_tunnelTcpClientStream.TcpClient.GetStream(), _orgTcpClientStream.Stream, false, -1);
            }
            OnThreadEnd();
        }

        private void TunnelWritingThread()
        {
            var read = CopyTo(_orgTcpClientStream.Stream, _tunnelTcpClientStream.Stream, true, _tlsLength);
            if (read == _tlsLength)
            {
                _tunnelTcpClientStream.Stream.Flush(); //flush last SSL buffer
                CopyTo(_orgTcpClientStream.Stream, _tunnelTcpClientStream.TcpClient.GetStream(), true, -1);
            }
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
        private int CopyTo(Stream soruce, Stream destination, bool isSendingOut, long maxBytes)
        {
            try
            {
                return CopyToInternal(soruce, destination, isSendingOut, maxBytes);
            }
            catch
            {
                return -2;
            }
        }

        // return total copied bytes
        private int CopyToInternal(Stream source, Stream destination, bool isSendingOut, long maxBytes)
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
        }
    }
}
