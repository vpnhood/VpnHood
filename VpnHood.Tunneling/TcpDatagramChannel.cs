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

        public event EventHandler OnFinished;
        public event EventHandler<ChannelPacketArrivalEventArgs> OnPacketArrival;
        public int SendBufferSize => _tcpClientStream.TcpClient.SendBufferSize;
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
                    OnPacketArrival?.Invoke(this, new ChannelPacketArrivalEventArgs(ipPackets, this));
                }
            }
            catch
            {
            }
            finally
            {
                Connected = false;
                if (!_disposed)
                    OnFinished?.Invoke(this, EventArgs.Empty);
            }
        }

        public void SendPackets(IPPacket[] ipPackets)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpDatagramChannel));

            var size = ipPackets.Sum(packet => packet.TotalPacketLength);
            var buffer = new byte[size];
            var destIndex = 0;
            foreach (var ipPacket in ipPackets)
            {
                var source = ipPacket.Bytes;
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
            _tcpClientStream = null;
        }
    }
}
