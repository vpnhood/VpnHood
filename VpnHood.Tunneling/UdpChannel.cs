using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.Logging;

namespace VpnHood.Tunneling
{
    public class UdpChannel : IDatagramChannel
    {
        private readonly byte[] _buffer = new byte[0xFFFF];
        private readonly BufferCryptor _bufferCryptor;
        private readonly int _bufferHeaderLength;
        private readonly long _cryptorPosBase;
        private readonly bool _isClient;
        private readonly object _lockCleanup = new();
        private readonly int _mtuWithFragmentation = TunnelUtil.MtuWithFragmentation;

        private readonly uint _sessionId;
        private readonly UdpClient _udpClient;
        private bool _disposed;
        private IPEndPoint? _lastRemoteEp;

        public UdpChannel(bool isClient, UdpClient udpClient, uint sessionId, byte[] key)
        {
            VhLogger.Instance.LogInformation(GeneralEventId.Udp, 
                $"Creating a {nameof(UdpChannel)}. SessiondId: {VhLogger.FormatSessionId(_sessionId)} ...");

            Key = key;
            _isClient = isClient;
            _cryptorPosBase = isClient ? 0 : long.MaxValue / 2;
            _bufferCryptor = new BufferCryptor(key);
            _sessionId = sessionId;
            _udpClient = udpClient;
            _bufferHeaderLength = _isClient
                ? 4 + 8 // client->server: sessionId + sentBytes (IV)
                : 8; // server->client: sentBytes(IV)

            //tunnel manages fragmentation; we just need to send it as possible
            if (udpClient.Client.AddressFamily == AddressFamily.InterNetwork)
                udpClient.DontFragment = false; // Never call this for IPv6, it will throw exception for any value
        }

        public byte[] Key { get; }
        public int LocalPort => ((IPEndPoint)_udpClient.Client.LocalEndPoint).Port;

        public event EventHandler<ChannelEventArgs>? OnFinished;
        public event EventHandler<ChannelPacketReceivedEventArgs>? OnPacketReceived;
        public bool Connected { get; private set; }
        public long SentByteCount { get; private set; }
        public long ReceivedByteCount { get; private set; }
        public DateTime LastActivityTime { get; private set; }

        public void Start()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpDatagramChannel));

            if (Connected)
                throw new Exception("Start has already been called!");

            Connected = true;
            _ = ReadTask();
        }

        public async Task SendPacketAsync(IPPacket[] ipPackets)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpDatagramChannel));

            // Tunnel optimizes the packets in regard of MTU without fragmentation 
            // so here we are not worry about optimizing it and can use fragmentation because the sum of 
            // packets should be small enough to fill a udp packet
            var maxDataLen = _mtuWithFragmentation - _bufferHeaderLength;
            var dataLen = ipPackets.Sum(x => x.TotalPacketLength);
            if (dataLen > maxDataLen)
                throw new InvalidOperationException(
                    $"Total packets length is too big for {VhLogger.FormatTypeName(this)}. MaxSize: {maxDataLen}, Packets Size: {dataLen} !");

            // copy packets to buffer
            var buffer = _buffer;
            var bufferIndex = _bufferHeaderLength;

            // add sessionId for verification
            BitConverter.GetBytes(_sessionId).CopyTo(buffer, bufferIndex);
            bufferIndex += 4;
            foreach (var ipPacket in ipPackets)
            {
                Buffer.BlockCopy(ipPacket.Bytes, 0, buffer, bufferIndex, ipPacket.TotalPacketLength);
                bufferIndex += ipPacket.TotalPacketLength;
            }

            await Send(buffer, bufferIndex);
        }

        private async Task ReadTask()
        {
            var ipPackets = new List<IPPacket>();

            // wait for all incoming UDP packets
            while (!_disposed)
            {
                try
                {
                    var udpResult = await _udpClient.ReceiveAsync();
                    _lastRemoteEp = udpResult.RemoteEndPoint;
                    var buffer = udpResult.Buffer;

                    // decrypt buffer
                    var bufferIndex = 0;
                    if (_isClient)
                    {
                        var cryptoPos = BitConverter.ToInt64(buffer, bufferIndex);
                        bufferIndex += 8;
                        _bufferCryptor.Cipher(buffer, bufferIndex, buffer.Length, cryptoPos);
                    }
                    else
                    {
                        var sessionId = BitConverter.ToUInt32(buffer, bufferIndex);
                        bufferIndex += 4;
                        if (sessionId != _sessionId)
                            throw new InvalidDataException("Invalid sessionId");

                        var cryptoPos = BitConverter.ToInt64(buffer, bufferIndex);
                        bufferIndex += 8;
                        _bufferCryptor.Cipher(buffer, bufferIndex, buffer.Length, cryptoPos);
                    }

                    // verify sessionId after cipher
                    var sessionId2 = BitConverter.ToUInt32(buffer, bufferIndex);
                    bufferIndex += 4;
                    if (sessionId2 != _sessionId)
                        throw new InvalidDataException("Invalid sessionId");

                    // read all packets
                    while (bufferIndex < buffer.Length)
                    {
                        var ipPacket = PacketUtil.ReadNextPacket(buffer, ref bufferIndex);
                        ReceivedByteCount += ipPacket.TotalPacketLength;
                        ipPackets.Add(ipPacket);
                    }
                }
                catch (Exception ex)
                {
                    if (IsInvalidState(ex))
                        Dispose();
                    else
                        VhLogger.Instance.Log(LogLevel.Warning, GeneralEventId.Udp,
                            $"Error in receiving packets! Error: {ex.Message}");
                }

                // send collected packets when there is no more packets in the UdpClient buffer
                if (!_disposed && _udpClient.Available == 0)
                {
                    FireReceivedPackets(ipPackets.ToArray());
                    ipPackets.Clear();
                }
            }

            Dispose();
        }

        private void FireReceivedPackets(IPPacket[] ipPackets)
        {
            if (_disposed)
                return;

            try
            {
                OnPacketReceived?.Invoke(this, new ChannelPacketReceivedEventArgs(ipPackets, this));
                LastActivityTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                VhLogger.Instance.Log(LogLevel.Warning, GeneralEventId.Udp,
                    $"Error in processing received packets! Error: {ex.Message}");
            }
        }

        private async Task Send(byte[] buffer, int bufferCount)
        {
            try
            {
                int ret;
                if (VhLogger.IsDiagnoseMode)
                    VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp,
                        $"{VhLogger.FormatTypeName(this)} is sending {bufferCount} bytes...");

                var cryptoPos = _cryptorPosBase + SentByteCount;
                _bufferCryptor.Cipher(buffer, _bufferHeaderLength, bufferCount, cryptoPos);
                if (_isClient)
                {
                    BitConverter.GetBytes(_sessionId).CopyTo(buffer, 0);
                    BitConverter.GetBytes(cryptoPos).CopyTo(buffer, 4);
                    ret = await _udpClient.SendAsync(buffer, bufferCount);
                }
                else
                {
                    BitConverter.GetBytes(cryptoPos).CopyTo(buffer, 0);
                    ret = await _udpClient.SendAsync(buffer, bufferCount, _lastRemoteEp);
                }

                if (ret != bufferCount)
                    throw new Exception(
                        $"{VhLogger.FormatTypeName(this)}: Send {ret} bytes instead {bufferCount} bytes! ");

                SentByteCount += ret;
                LastActivityTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                VhLogger.Instance.Log(LogLevel.Error, GeneralEventId.Udp,
                    $"{VhLogger.FormatTypeName(this)}: Could not send {bufferCount} packets! Message: {ex.Message}");
                if (IsInvalidState(ex))
                    Dispose();
            }
        }

        private bool IsInvalidState(Exception ex)
        {
            return _disposed || ex is ObjectDisposedException or SocketException { SocketErrorCode: SocketError.InvalidArgument };
        }

        public void Dispose()
        {
            lock (_lockCleanup)
            {
                if (_disposed) return;
                _disposed = true;
            }

            VhLogger.Instance.LogInformation(GeneralEventId.Udp, 
                $"Disposing a {nameof(UdpChannel)}. SessiondId: {VhLogger.FormatSessionId(_sessionId)} ...");

            Connected = false;
            _bufferCryptor.Dispose();
            _udpClient.Dispose();

            OnFinished?.Invoke(this, new ChannelEventArgs(this));
        }
    }
}