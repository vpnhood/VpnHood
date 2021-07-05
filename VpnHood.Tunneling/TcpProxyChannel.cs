using System;
using System.IO;
using System.Threading;

namespace VpnHood.Tunneling
{
    public class TcpProxyChannel : IChannel
    {
        private readonly TcpClientStream _orgTcpClientStream;
        private readonly TcpClientStream _tunnelTcpClientStream;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private Thread _tunnelReadingThread;
        private Thread _tunnelWritingThread;
        private bool _disposed = false;

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
            _tunnelReadingThread = new Thread(TunnelReadingProc, TunnelUtil.SocketStackSize_Stream); //StackSize must be optimized!
            _tunnelWritingThread = new Thread(TunnelWritingProc, TunnelUtil.SocketStackSize_Stream); //StackSize must be optimized!
            _tunnelReadingThread.Start();
            _tunnelWritingThread.Start();
        }
        private void TunnelReadingProc(object obj)
        {
            CopyTo(_tunnelTcpClientStream.Stream, _orgTcpClientStream.Stream, false);
        }

        private void TunnelWritingProc(object obj)
        {
            CopyTo(_orgTcpClientStream.Stream, _tunnelTcpClientStream.Stream, true);
        }

        private void OnThreadEnd()
        {
            Dispose();
        }

        private void CopyTo(Stream soruce, Stream destination, bool isSendingOut)
        {
            try
            {
                CopyToInternal(soruce, destination, isSendingOut);
            }
            catch
            {
                Dispose();
            }
            finally
            {
                OnThreadEnd();
            }
        }

        private void CopyToInternal(Stream source, Stream destination, bool isSendingOut)
        {
            // Microsoft Stream Source Code:
            // We pick a value that is the largest multiple of 4096 that is still smaller than the large object heap threshold (85K).
            // The CopyTo/CopyToAsync buffer is short-lived and is likely to be collected at Gen0, and it offers a significant
            // improvement in Copy performance.
            const int bufferSize = 81920; // recommended by microsoft for copying buffers
            var readBuffer = new byte[bufferSize];
            int totalRead = 0;
            int bytesRead;
            while (true)
            {
                bytesRead = source.Read(readBuffer, 0, readBuffer.Length);
                if (bytesRead == 0)
                    break; // end of the stream

                totalRead += bytesRead;

                if (!isSendingOut)
                    ReceivedByteCount += bytesRead;

                destination.Write(readBuffer, 0, bytesRead);

                if (isSendingOut)
                    SentByteCount += bytesRead;
            }
        }

        private readonly object _lockCleanup = new();
        public void Dispose()
        {
            lock (_lockCleanup)
            {
                if (_disposed) return;
                _disposed = true;
            }

            Connected = false;
            _cancellationTokenSource.Cancel();
            _orgTcpClientStream.Dispose();
            _tunnelTcpClientStream.Dispose();
            OnFinished?.Invoke(this, EventArgs.Empty);
        }
    }
}
