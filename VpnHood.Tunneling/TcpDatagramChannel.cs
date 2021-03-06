﻿using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
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

        public event EventHandler OnFinished;
        public event EventHandler<ChannelPacketReceivedEventArgs> OnPacketReceived;
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
                OnFinished?.Invoke(this, EventArgs.Empty);
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

        public void SendPacket(IEnumerable<IPPacket> ipPackets)
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
        }
    }
}
