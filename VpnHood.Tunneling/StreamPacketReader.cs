using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using PacketDotNet;
using PacketDotNet.Utils;

namespace VpnHood.Tunneling
{
    public class StreamPacketReader
    {
        private readonly byte[] _buffer = new byte[1500 * 100];
        private readonly List<IPPacket> _ipPackets = new();
        private readonly Stream _stream;
        private int _bufferCount;

        public StreamPacketReader(Stream stream)
        {
            _stream = stream;
        }


        /// <returns>null if read nothing</returns>
        public async Task<IPPacket[]?> ReadAsync()
        {
            _ipPackets.Clear();

            var moreData = true;
            while (moreData)
            {
                var toRead = _buffer.Length - _bufferCount;
                var read = await _stream.ReadAsync(_buffer, _bufferCount, toRead);
                _bufferCount += read;
                moreData = toRead == read && read != 0;

                // read packet size
                var bufferIndex = 0;
                while (_bufferCount - bufferIndex >= 4)
                {
                    var version = _buffer[bufferIndex] >> 4;
                    var packetLength = version == 4
                        ? IPAddress.NetworkToHostOrder(BitConverter.ToInt16(_buffer, bufferIndex + 2))
                        : 40;
                    if (packetLength < 20) //v4
                        throw new Exception($"A packet with invalid length has been received! Length: {packetLength}");

                    // read all packets
                    if (_bufferCount - bufferIndex < packetLength)
                        break;

                    var packetBuffer =
                        _buffer[bufferIndex..(bufferIndex + packetLength)]; //we shouldn't use shared memory for packet
                    var ipPacket = Packet.ParsePacket(LinkLayers.Raw, packetBuffer).Extract<IPPacket>();
                    _ipPackets.Add(ipPacket);

                    bufferIndex += packetLength;
                }

                // shift the rest buffer to start
                if (_bufferCount > bufferIndex)
                    Array.Copy(_buffer, bufferIndex, _buffer, 0, _bufferCount - bufferIndex);
                _bufferCount -= bufferIndex;

                //end of last packet aligned to last byte of buffer, so maybe there is no more packet
                if (_bufferCount == 0)
                    moreData = false;
            }

            return _ipPackets.Count != 0 ? _ipPackets.ToArray() : null;
        }
    }
}