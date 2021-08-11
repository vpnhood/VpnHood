using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VpnHood.Logging;

namespace VpnHood.Tunneling
{
    public class TcpDatagramChannel : IDatagramChannel
    {
        private readonly TcpClientStream _tcpClientStream;
        private readonly int _mtu = 0xFFFF;
        private readonly byte[] _buffer = new byte[0xFFFF];
        private readonly object _sendLock = new();

        public event EventHandler<ChannelEventArgs>? OnFinished;
        public event EventHandler<ChannelPacketReceivedEventArgs>? OnPacketReceived;
        public bool Connected { get; private set; }
        public long SentByteCount { get; private set; }
        public long ReceivedByteCount { get; private set; }
        public DateTime LastActivityTime { get; private set; } = DateTime.Now;

        public TcpDatagramChannel(TcpClientStream tcpClientStream)
        {
            _tcpClientStream = tcpClientStream ?? throw new ArgumentNullException(nameof(tcpClientStream));
            tcpClientStream.TcpClient.NoDelay = true;
        }

        public void Start()
        {
            if (Connected)
                throw new Exception("Start has already been called!");

            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpDatagramChannel));

            Connected = true;
            _ = ReadTask();
        }

        private async Task ReadTask()
        {
            var tcpClient = _tcpClientStream.TcpClient;
            var stream = _tcpClientStream.Stream;

            try
            {
                var streamPacketReader = new StreamPacketReader(stream);
                while (tcpClient.Connected)
                {
                    var ipPackets = await streamPacketReader.ReadAsync();
                    if (ipPackets == null || _disposed)
                        break;

                    LastActivityTime = DateTime.Now;
                    ReceivedByteCount += ipPackets.Sum(x => x.TotalPacketLength);
                    FireReceivedPackets(ipPackets);
                }
            }
            catch
            {
            }
            finally
            {
                Dispose();
            }
        }

        private void FireReceivedPackets(IEnumerable<IPPacket> ipPackets)
        {
            if (_disposed)
                return;

            try
            {
                OnPacketReceived?.Invoke(this, new ChannelPacketReceivedEventArgs(ipPackets, this));
            }
            catch (Exception ex)
            {
                VhLogger.Instance.Log(LogLevel.Warning, GeneralEventId.Udp, $"Error in processing received packets! Error: {ex.Message}");
            }
        }

        public async Task SendPacketAsync(IEnumerable<IPPacket> ipPackets)
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

            foreach (var ipPacket in ipPackets)
            {
                Buffer.BlockCopy(ipPacket.Bytes, 0, buffer, bufferIndex, ipPacket.TotalPacketLength);
                bufferIndex += ipPacket.TotalPacketLength;
            }
            await _tcpClientStream.Stream.WriteAsync(buffer, 0, bufferIndex);
            LastActivityTime = DateTime.Now;
            SentByteCount += bufferIndex;
        }

        private bool _disposed = false;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Connected = false;
            _tcpClientStream.Dispose();

            OnFinished?.Invoke(this, new ChannelEventArgs(this));
        }
    }
}
