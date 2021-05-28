using Microsoft.Extensions.Logging;
using PacketDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using VpnHood.Logging;

namespace VpnHood.Tunneling
{
    public class UdpChannel : IDatagramChannel
    {
        private Thread _thread;
        private IPEndPoint _lastRemoteEp;
        private readonly UdpClient _udpClient;
        private readonly int _mtu = 0xFFFF - 28;
        private readonly int _bufferHeaderLength;
        private readonly int _sessionId;
        private readonly bool _isClient;
        private readonly BufferCryptor _bufferCryptor;

        public event EventHandler OnFinished;
        public event EventHandler<ChannelPacketArrivalEventArgs> OnPacketArrival;

        public bool Connected { get; private set; }
        public int SendBufferSize => _mtu - _bufferHeaderLength;
        public long SentByteCount { get; private set; }
        public long ReceivedByteCount { get; private set; }
        public int LocalPort => ((IPEndPoint)_udpClient.Client.LocalEndPoint).Port;

        public UdpChannel(bool isClient, UdpClient udpClient, int sessionId, byte[] encKey)
        {
            _isClient = isClient;
            _bufferCryptor = new BufferCryptor(encKey);
            _sessionId = sessionId;
            _udpClient = udpClient;
            _bufferHeaderLength = _isClient
                ? 4 + 8  // client->server: sessionId + sentBytes (IV)
                : 8; // server->client: sentBytes(IV)

            Connected = true;
        }

        public void Start()
        {
            if (_thread != null)
                throw new Exception("Start has already been called!");

            if (_disposed)
                throw new ObjectDisposedException(nameof(TcpDatagramChannel));

            _thread = new Thread(ReadThread, TunnelUtil.SocketStackSize_Datagram);
            _thread.Start();
        }

        private void ReadThread(object obj)
        {
            try
            {
                var ipPackets = new List<IPPacket>();

                // wait for all incoming UDP packets
                while (!_disposed)
                {
                    // read all packets in buffer
                    while (!_disposed)
                    {
                        var buffer = _udpClient.Receive(ref _lastRemoteEp);
                        ReceivedByteCount += buffer.Length;

                        // decrypt buffer
                        var bufferIndex = 0;
                        if (_isClient)
                        {
                            long streamPos = BitConverter.ToInt64(buffer, 0);
                            bufferIndex = 8;
                            _bufferCryptor.Cipher(buffer, bufferIndex, buffer.Length, streamPos);
                        }
                        else
                        {
                            long sessionId = BitConverter.ToInt32(buffer, 0);
                            long streamPos = BitConverter.ToInt64(buffer, 4);
                            bufferIndex = 12;
                            _bufferCryptor.Cipher(buffer, bufferIndex, buffer.Length, streamPos);
                        }

                        // read all packets
                        while (bufferIndex < buffer.Length)
                        {
                            try
                            {
                                var ipPacket = TunnelUtil.ReadNextPacket(buffer, ref bufferIndex);
                                ipPackets.Add(ipPacket);
                            }
                            catch (Exception ex)
                            {
                                VhLogger.Current.LogWarning($"Invalid udp packet has been received! er: {ex.Message}");
                            }
                        }

                        // send collected packets when there is no more packets in the buffer
                        if (_udpClient.Available == 0)
                            break;
                    }

                    OnPacketArrival?.Invoke(this, new ChannelPacketArrivalEventArgs(ipPackets.ToArray(), this));
                    ipPackets.Clear();
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

            var buffer = new byte[_mtu];
            var bufferIndex = _bufferHeaderLength;
            var maxDataLen = SendBufferSize;

            foreach (var ipPacket in ipPackets)
            {
                var packetBuffer = ipPacket.Bytes;
                if (packetBuffer.Length > maxDataLen)
                {
                    VhLogger.Current.LogWarning($"Packet too larget! A packet has beem dropped by {VhLogger.FormatTypeName(this)}. PacketSize: {packetBuffer.Length}");
                    continue; //ignore packet
                }

                // send previous buffer if there is no more space
                if (bufferIndex + packetBuffer.Length > maxDataLen)
                {
                    Send(buffer, bufferIndex);
                    bufferIndex = _bufferHeaderLength;
                }

                // add packet to buffer
                Buffer.BlockCopy(packetBuffer, 0, buffer, bufferIndex, packetBuffer.Length);
                bufferIndex += packetBuffer.Length;
            }

            // send the rest of buffer
            if (bufferIndex > _bufferHeaderLength)
            {
                Send(buffer, bufferIndex);
                bufferIndex = _bufferHeaderLength;
            }
        }

        private int Send(byte[] buffer, int bufferCount)
        {
            int ret;
            _bufferCryptor.Cipher(buffer, _bufferHeaderLength, bufferCount, SentByteCount);

            if (_isClient)
            {
                BitConverter.GetBytes(_sessionId).CopyTo(buffer, 0);
                BitConverter.GetBytes(SentByteCount).CopyTo(buffer, 4);
                ret = _udpClient.Send(buffer, bufferCount);
            }
            else
            {
                BitConverter.GetBytes(SentByteCount).CopyTo(buffer, 0);
                ret = _udpClient.Send(buffer, bufferCount, _lastRemoteEp);
            }

            SentByteCount += ret;
            return ret;
        }

        private bool _disposed = false;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Connected = false;
            _bufferCryptor.Dispose();
        }
    }
}
