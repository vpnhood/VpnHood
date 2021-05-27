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
        private readonly ICryptoTransform _crypto;

        public event EventHandler OnFinished;
        public event EventHandler<ChannelPacketArrivalEventArgs> OnPacketArrival;


        public bool Connected { get; private set; }
        public int SendBufferSize => _mtu - _bufferHeaderLength;
        public long SentByteCount { get; private set; }
        public long ReceivedByteCount { get; private set; }

        public UdpChannel(bool isClient, UdpClient udpClient, int sessionId, byte[] encKey)
        {
            _isClient = isClient;
            _sessionId = sessionId;
            _udpClient = udpClient;
            _bufferHeaderLength = _isClient 
                ? 4 + 8  // client->server: sessionId + sentBytes (IV)
                : 8; // server->client: sentBytes(IV)

            //init cryptor
            using var aes = Aes.Create();
            aes.KeySize = encKey.Length * 8;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = encKey;
            _crypto = aes.CreateEncryptor(aes.Key, aes.IV);

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

                        // read all packets
                        var bufferIndex = 0;
                        while (bufferIndex < buffer.Length)
                        {
                            try
                            {
                                if (_isClient)
                                {
                                    long streamPos = BitConverter.ToInt64(buffer, 0);
                                    Cipher(buffer, 8, buffer.Length, streamPos);
                                }
                                else
                                {
                                    long sessionId = BitConverter.ToInt32(buffer, 0);
                                    long streamPos = BitConverter.ToInt64(buffer, 4);
                                    Cipher(buffer, 12, buffer.Length, streamPos);
                                }

                                // read packet length
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
            var bufferIndex = 0;

            foreach (var ipPacket in ipPackets)
            {
                var packetBuffer = ipPacket.Bytes;
                if (packetBuffer.Length > buffer.Length)
                {
                    VhLogger.Current.LogWarning($"Packet too larget! A packet has beem dropped by {VhLogger.FormatTypeName(this)}. PacketSize: {packetBuffer.Length}");
                    continue; //ignore packet
                }

                // send previous buffer if there is no more space
                if (bufferIndex + packetBuffer.Length > buffer.Length)
                {
                    Send(buffer, bufferIndex);
                    bufferIndex = 0;
                }

                // add packet to buffer
                Buffer.BlockCopy(packetBuffer, 0, buffer, bufferIndex, packetBuffer.Length);
                bufferIndex += packetBuffer.Length;
            }

            // send the rest of buffer
            if (bufferIndex > 0)
            {
                Send(buffer, bufferIndex);
                bufferIndex = 0;
            }
        }

        private int Send(byte[] buffer, int bufferCount)
        {
            int ret;
            Cipher(buffer, _bufferHeaderLength, bufferCount, SentByteCount);

            if (_isClient)
            {
                BitConverter.GetBytes(_sessionId).CopyTo(buffer, 0);
                BitConverter.GetBytes(SentByteCount).CopyTo(buffer, 0);
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

        private void Cipher(byte[] buffer, int offset, int count, long streamPos)
        {
            //find block number
            var blockSizeInByte = _crypto.OutputBlockSize / 8;
            var blockNumber = (streamPos / blockSizeInByte) + 1;
            var keyPos = streamPos % blockSizeInByte;

            //buffer
            var outBuffer = new byte[blockSizeInByte];
            var nonce = new byte[blockSizeInByte];
            var init = false;

            for (int i = offset; i < count; i++)
            {
                //encrypt the nonce to form next xor buffer (unique key)
                if (!init || (keyPos % blockSizeInByte) == 0)
                {
                    BitConverter.GetBytes(blockNumber).CopyTo(nonce, 0);
                    _crypto.TransformBlock(nonce, 0, nonce.Length, outBuffer, 0);
                    if (init) keyPos = 0;
                    init = true;
                    blockNumber++;
                }
                buffer[i] ^= outBuffer[keyPos]; //simple XOR with generated unique key
                keyPos++;
            }
        }

        private bool _disposed = false;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Connected = false;
            _crypto.Dispose();
        }
    }
}
