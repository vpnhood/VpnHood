using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VpnHood.Tunneling
{
    public class TcpProxyChannel : IChannel
    {
        private readonly int BufferSize_Max = 0x14000 * 2;
        private readonly TcpClientStream _orgTcpClientStream;
        private readonly TcpClientStream _tunnelTcpClientStream;
        private readonly int _orgStreamReadBufferSize;
        private readonly int _tunnelStreamReadBufferSize;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private bool _disposed = false;

        public event EventHandler<ChannelEventArgs>? OnFinished;
        public bool Connected { get; private set; }
        public long SentByteCount { get; private set; }
        public long ReceivedByteCount { get; private set; }
        public DateTime LastActivityTime { get; private set; } = DateTime.Now;

        public TcpProxyChannel(TcpClientStream orgTcpClientStream, TcpClientStream tunnelTcpClientStream,
            int orgStreamReadBufferSize = TunnelUtil.StreamBufferSize, int tunnelStreamReadBufferSize = TunnelUtil.StreamBufferSize)
        {
            _orgTcpClientStream = orgTcpClientStream ?? throw new ArgumentNullException(nameof(orgTcpClientStream));
            _tunnelTcpClientStream = tunnelTcpClientStream ?? throw new ArgumentNullException(nameof(tunnelTcpClientStream));

            _orgStreamReadBufferSize = orgStreamReadBufferSize > 0 && orgStreamReadBufferSize <= BufferSize_Max
                ? orgStreamReadBufferSize
                : throw new ArgumentOutOfRangeException($"Value must greater than 0 and less than {BufferSize_Max}", orgStreamReadBufferSize, nameof(orgStreamReadBufferSize));

            _tunnelStreamReadBufferSize = tunnelStreamReadBufferSize > 0 && tunnelStreamReadBufferSize <= BufferSize_Max
                ? tunnelStreamReadBufferSize
                : throw new ArgumentOutOfRangeException($"Value must greater than 0 and less than {BufferSize_Max}", tunnelStreamReadBufferSize, nameof(tunnelStreamReadBufferSize));
        }

        public void Start()
        {
            Connected = true;

            _ = CopyToAsync(_tunnelTcpClientStream.Stream, _orgTcpClientStream.Stream, false, _tunnelStreamReadBufferSize); // read
            _ = CopyToAsync(_orgTcpClientStream.Stream, _tunnelTcpClientStream.Stream, true, _orgStreamReadBufferSize); //write
        }

        private async Task CopyToAsync(Stream soruce, Stream destination, bool isSendingOut, int bufferSize)
        {
            try
            {
                await CopyToInternalAsync(soruce, destination, isSendingOut, bufferSize);
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

        private async Task CopyToInternalAsync(Stream source, Stream destination, bool isSendingOut, int bufferSize)
        {
            const bool doubleBuffer = false; //i am not sure it could help!
            var cancellationToken = _cancellationTokenSource.Token;

            // Microsoft Stream Source Code:
            // We pick a value that is the largest multiple of 4096 that is still smaller than the large object heap threshold (85K).
            // The CopyTo/CopyToAsync buffer is short-lived and is likely to be collected at Gen0, and it offers a significant
            // improvement in Copy performance.
            // 0x14000 recommended by microsoft for copying buffers
            // const int bufferSize = 0x14000 / 4;  
            if (bufferSize > BufferSize_Max)
                throw new ArgumentException($"Buffer is too big, maximum supported size is {BufferSize_Max}", nameof(bufferSize));

            // <<----------------- the MOST memory consuming in the APP! >> ----------------------
            var readBuffer = new byte[bufferSize];
            var writeBuffer = doubleBuffer ? new byte[readBuffer.Length] : null;
            var totalRead = 0;
            int bytesRead;
            Task? writeTask = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                // read from source
                bytesRead = await source.ReadAsync(readBuffer, 0, readBuffer.Length, cancellationToken);
                if (writeTask != null)
                    await writeTask;

                // check end of the stream
                if (bytesRead == 0)
                    break;

                // write to destination
                if (writeBuffer != null)
                {
                    Array.Copy(readBuffer, writeBuffer, bytesRead);
                    writeTask = destination.WriteAsync(writeBuffer, 0, bytesRead, cancellationToken);
                }
                else
                {
                    await destination.WriteAsync(readBuffer, 0, bytesRead, cancellationToken);
                }

                // calculate transferred bytes
                totalRead += bytesRead;
                if (!isSendingOut)
                    ReceivedByteCount += bytesRead;
                else
                    SentByteCount += bytesRead;

                // set LastActivityTime as some data delegated
                LastActivityTime = DateTime.Now;
            }
        }

        private void OnThreadEnd()
        {
            Dispose();
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

            OnFinished?.Invoke(this, new(this));
        }
    }
}
