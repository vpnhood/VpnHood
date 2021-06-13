using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.Linq;
using System.Net;
using System.Threading;
using VpnHood.Logging;

namespace VpnHood.Tunneling
{
    public class TcpDatagramChannel : IDatagramChannel
    {
        private TcpClientStream _tcpClientStream;
        private Thread _thread;
        private readonly int _mtu = 0xFFFF;
        private readonly byte[] _buffer = new byte[0xFFFF];

        public event EventHandler OnFinished;
        public event EventHandler<ChannelPacketArrivalEventArgs> OnPacketReceived;
        public bool Connected { get; private set; }
        public long SentByteCount { get; private set; }
        public long ReceivedByteCount { get; private set; }

        public TcpDatagramChannel(TcpClientStream tcpClientStream)
        {
            _tcpClientStream = tcpClientStream ?? throw new ArgumentNullException(nameof(tcpClientStream));
            tcpClientStream.TcpClient.NoDelay = true;
        }

        public void Start()
        {
            if (_thread != null)
                throw new Exception("Start has already been called!");

            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpDatagramChannel));

            Connected = true;
            _thread = new Thread(ReadThread, TunnelUtil.SocketStackSize_Stream); //256K
            _thread.Start();
        }

        private void ReadThread(object obj)
        {
            var tcpClient = _tcpClientStream.TcpClient;
            var stream = _tcpClientStream.Stream;

            try
            {
                var streamPacketReader = new StreamPacketReader(stream);
                while (tcpClient.Connected)
                {
                    var ipPackets = streamPacketReader.Read();
                    if (ipPackets.Length == 0 || _disposed)
                        break;

                    ReceivedByteCount += ipPackets.Sum(x => x.TotalPacketLength);
                    OnPacketReceived?.Invoke(this, new ChannelPacketArrivalEventArgs(ipPackets, this));
                }
            }
            catch
            {
            }
            finally
            {
                Dispose();
                OnFinished?.Invoke(this, EventArgs.Empty);
            }
        }

        private readonly object _sendLock = new();
        public void SendPackets(IPPacket[] ipPackets)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpDatagramChannel));

            var maxDataLen = _mtu;
            var dataLen = ipPackets.Sum(x => x.TotalPacketLength);
            if (dataLen > maxDataLen)
                throw new InvalidOperationException($"Total packets length is too big for {VhLogger.FormatTypeName(this)}. MaxSize: {maxDataLen}, Packets Size: {dataLen} !");

            // copy packets to buffer
            var buffer = _buffer;
            var bufferIndex = 0;

            lock (_sendLock) //access to the shared buffer
            {
                foreach (var ipPacket in ipPackets)
                {
                    Buffer.BlockCopy(ipPacket.Bytes, 0, buffer, bufferIndex, ipPacket.TotalPacketLength);
                    bufferIndex += ipPacket.TotalPacketLength;
                }
                _tcpClientStream.Stream.Write(buffer, 0, bufferIndex);
                SentByteCount += bufferIndex;
            }
        }

        private bool _disposed = false;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Connected = false;
            _tcpClientStream.Dispose();
            _tcpClientStream = null;
        }
    }
}
