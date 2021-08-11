using Microsoft.Extensions.Logging;
using PacketDotNet;
using PacketDotNet.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using VpnHood.Logging;

namespace VpnHood.Tunneling
{
    public class UdpChannel : IDatagramChannel
    {
        private IPEndPoint? _lastRemoteEp;
        private readonly UdpClient _udpClient;
        private readonly int _mtuWithFragmentation = TunnelUtil.MtuWithFragmentation;
        private readonly int _bufferHeaderLength;
        private readonly int _sessionId;
        private readonly bool _isClient;
        private readonly BufferCryptor _bufferCryptor;
        private readonly long _cryptorPosBase;
        private readonly IPAddress _selfEchoAddress = IPAddress.Parse("0.0.0.0");
        private readonly byte[] _selfEchoPayload = new byte[1200];
        private readonly byte[] _buffer = new byte[0xFFFF];
        private bool _disposed = false;
        private readonly object _lockCleanup = new();

        public event EventHandler<ChannelEventArgs>? OnFinished;
        public event EventHandler<ChannelPacketReceivedEventArgs>? OnPacketReceived;
        public event EventHandler? OnSelfEchoReply;

        public byte[] Key { get; private set; }
        public bool Connected { get; private set; }
        public long SentByteCount { get; private set; }
        public long ReceivedByteCount { get; private set; }
        public int LocalPort => ((IPEndPoint)_udpClient.Client.LocalEndPoint).Port;
        public DateTime LastActivityTime { get; private set; }

        public UdpChannel(bool isClient, UdpClient udpClient, int sessionId, byte[] key)
        {
            Key = key;
            _isClient = isClient;
            _cryptorPosBase = isClient ? 0 : long.MaxValue / 2;
            _bufferCryptor = new BufferCryptor(key);
            _sessionId = sessionId;
            _udpClient = udpClient;
            _bufferHeaderLength = _isClient
                ? 4 + 8  // client->server: sessionId + sentBytes (IV)
                : 8; // server->client: sentBytes(IV)

            // fill echo payload with 1
            var random = new Random();
            _selfEchoPayload = new byte[random.Next(50, 1200)];
            for (var i = 0; i < _selfEchoPayload.Length; i++)
                _selfEchoPayload[i] = 1;

            //tunnel manages fragmentation; we just need to send it as possible
            udpClient.DontFragment = false;
        }

        public void Start()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpDatagramChannel));

            if (Connected)
                throw new Exception("Start has already been called!");

            Connected = true;
            _ = ReadTask();
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
                        var sessionId = BitConverter.ToInt32(buffer, bufferIndex);
                        bufferIndex += 4;
                        if (sessionId != _sessionId)
                            throw new InvalidDataException("Invalid sessionId");

                        var cryptoPos = BitConverter.ToInt64(buffer, bufferIndex);
                        bufferIndex += 8;
                        _bufferCryptor.Cipher(buffer, bufferIndex, buffer.Length, cryptoPos);
                    }

                    // read all packets
                    while (bufferIndex < buffer.Length)
                    {
                        var ipPacket = PacketUtil.ReadNextPacket(buffer, ref bufferIndex);
                        ReceivedByteCount += ipPacket.TotalPacketLength;

                        // check SelfEcho
                        //if (ipPacket.Protocol == PacketDotNet.ProtocolType.Icmp)
                        //{
                        //    if (CheckSelfEchoRequest(ipPacket) || CheckselfEchoReply(ipPacket))
                        //        continue;
                        //}

                        ipPackets.Add(ipPacket);
                    }
                }
                catch (Exception ex)
                {
                    VhLogger.Instance.Log(LogLevel.Warning, GeneralEventId.Udp, $"Error in receiving packets! Error: {ex.Message}");
                    if (IsInvalidState(ex))
                        Dispose();
                }

                // send collected packets when there is no more packets in the UdpClient buffer
                if (!_disposed && _udpClient.Available == 0)
                {
                    FireReceivedPackets(ipPackets);
                    ipPackets.Clear();
                }
            }

            Dispose();
        }

        private void FireReceivedPackets(IEnumerable<IPPacket> ipPackets)
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
                VhLogger.Instance.Log(LogLevel.Warning, GeneralEventId.Udp, $"Error in processing received packets! Error: {ex.Message}");
            }
        }

        private bool CheckSelfEchoRequest(IPPacket ipPacket)
        {
            if (ipPacket.Protocol != PacketDotNet.ProtocolType.Icmp || !ipPacket.DestinationAddress.Equals(_selfEchoAddress))
                return false;

            var t = ipPacket.SourceAddress;
            ipPacket.SourceAddress = ipPacket.DestinationAddress;
            ipPacket.DestinationAddress = t;
            //SendPacket(new[] { ipPacket });
            return true;
        }

        private bool CheckselfEchoReply(IPPacket ipPacket)
        {
            if (ipPacket.Protocol != PacketDotNet.ProtocolType.Icmp || !ipPacket.SourceAddress.Equals(_selfEchoAddress))
                return false;

            // extract time
            var icmpPacket = PacketUtil.ExtractIcmp(ipPacket);
            var buffer = icmpPacket.PayloadData;
            if (buffer.Length < _selfEchoPayload.Length)
                return false; //invalid size

            var time = DateTime.FromBinary(BitConverter.ToInt64(buffer, 0));
            if ((DateTime.Now - time).TotalSeconds > 10)
                return false; // expired time

            for (var i = 4; i < buffer.Length; i++)
                if (_selfEchoPayload[i] != buffer[i])
                    return false; //invalid data

            // Fire Echo Event
            OnSelfEchoReply?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void SendSelfEchoRequest()
        {
            // create Icmp packet with current time and 1
            var ipPacket = new IPv4Packet(_selfEchoAddress, _selfEchoAddress);
            var buffer = _selfEchoPayload;
            for (var i = 0; i < buffer.Length; i++)
                buffer[i] = 1;
            BitConverter.GetBytes(DateTime.UtcNow.ToBinary()).CopyTo(buffer, 0);
            var byteArraySegment = new ByteArraySegment(buffer);
            var icmpPacket = new IcmpV4Packet(byteArraySegment);
            ipPacket.ParentPacket = icmpPacket;
            PacketUtil.UpdateIpPacket(ipPacket);

            // send packet
            //SendPacket(new[] { ipPacket });
        }

        private readonly object _sendLock = new();
        public async Task SendPacketAsync(IEnumerable<IPPacket> ipPackets)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpDatagramChannel));

            // Tunnel optimises the packets in regard of MTU without fragmentation 
            // so here we are not worry about optimising it and can use fragmentation because the sum of 
            // packets should be small enought to fill a udp packet
            var maxDataLen = _mtuWithFragmentation - _bufferHeaderLength;
            var dataLen = ipPackets.Sum(x => x.TotalPacketLength);
            if (dataLen > maxDataLen)
                throw new InvalidOperationException($"Total packets length is too big for {VhLogger.FormatTypeName(this)}. MaxSize: {maxDataLen}, Packets Size: {dataLen} !");

            // copy packets to buffer
            var buffer = _buffer;
            var bufferIndex = _bufferHeaderLength;

            foreach (var ipPacket in ipPackets)
            {
                Buffer.BlockCopy(ipPacket.Bytes, 0, buffer, bufferIndex, ipPacket.TotalPacketLength);
                bufferIndex += ipPacket.TotalPacketLength;
            }
            await Send(buffer, bufferIndex);
        }

        private async Task<int> Send(byte[] buffer, int bufferCount)
        {
            try
            {
                int ret;
                if (VhLogger.IsDiagnoseMode)
                    VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.Udp, $"{VhLogger.FormatTypeName(this)} is sending {bufferCount} bytes...");

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
                    throw new Exception($"{VhLogger.FormatTypeName(this)}: Send {ret} bytes instead {bufferCount} bytes! ");

                SentByteCount += ret;
                LastActivityTime = DateTime.Now;
                return ret;
            }
            catch (Exception ex)
            {
                VhLogger.Instance.Log(LogLevel.Error, GeneralEventId.Udp, $"{VhLogger.FormatTypeName(this)}: Could not send {bufferCount} packets! Message: {ex.Message}");
                if (IsInvalidState(ex))
                    Dispose();
                return 0;
            }
        }

        private bool IsInvalidState(Exception ex) =>
            _disposed ||
            (ex is ObjectDisposedException ||
            (ex is SocketException socketException && socketException.SocketErrorCode == SocketError.InvalidArgument));

        public void Dispose()
        {
            lock (_lockCleanup)
            {
                if (_disposed) return;
                _disposed = true;
            }

            Connected = false;
            _bufferCryptor.Dispose();
            _udpClient.Dispose();

            OnFinished?.Invoke(this, new ChannelEventArgs(this));
        }
    }
}
