using Microsoft.Extensions.Logging;
using PacketDotNet;
using PacketDotNet.Utils;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace VpnHood
{
    public class TcpDatagramChannel : IDatagramChannel
    {
        private readonly TcpClientStream _tcpClientStream;
        private Thread _thread;

        public event EventHandler OnFinished;
        public event EventHandler<ChannelPacketArrivalEventArgs> OnPacketArrival;
        public int SendBufferSize => _tcpClientStream.TcpClient.SendBufferSize;

        // SentByteCount
        private readonly object _lockObject = new object();
        private bool _connected;
        public bool Connected
        {
            get { lock (_lockObject) return _connected; }
            private set { lock (_lockObject) _connected = value; }
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
            _thread = new Thread(ReadThread, Util.SocketStackSize_Stream); //256K
            _thread.Start();
        }

        private void ReadThread(object obj)
        {
            var tcpClient = _tcpClientStream.TcpClient;
            var stream = _tcpClientStream.Stream;

            var buffer = new byte[0xFFFF];
            try
            {
                while (tcpClient.Connected)
                {
                    var ipPacket = Util.Stream_ReadIpPacket(stream, buffer);
                    if (ipPacket == null)
                        break;

                    ReceivedByteCount += ipPacket.TotalPacketLength;
                    OnPacketArrival?.Invoke(this, new ChannelPacketArrivalEventArgs(ipPacket, this));
                }
            }
            catch
            {
            }
            finally
            {
                Connected = false;
                OnFinished?.Invoke(this, EventArgs.Empty);
            }
        }

        public void SendPacket(IPPacket[] packets)
        {
            var size = packets.Sum(packet => packet.TotalPacketLength);

            var buffer = new byte[size];
            var destIndex = 0;
            foreach (var packet in packets)
            {
                var source = packet.Bytes;
                Buffer.BlockCopy(source, 0, buffer, destIndex, source.Length);
                destIndex += source.Length;
            }

            _tcpClientStream.Stream.Write(buffer, 0, buffer.Length);
            SentByteCount += buffer.Length;
        }

        private bool _disposed = false;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Connected = false;

            _tcpClientStream.Dispose();
        }

    }
}
