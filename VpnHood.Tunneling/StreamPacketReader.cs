using PacketDotNet;
using PacketDotNet.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace VpnHood.Tunneling
{
    public class StreamPacketReader
    {
        private int _bufferCount = 0;
        private readonly byte[] _buffer = new byte[1500 * 200];
        private readonly List<IPPacket> _ipPackets = new();
        private readonly Stream _stream;

        public StreamPacketReader(Stream stream)
        {
            _stream = stream;
        }

        public IPPacket[] Read()
        {
            _ipPackets.Clear();

            var moreData = true;
            while (moreData)
            {
                var toRead = _buffer.Length - _bufferCount;
                var read = _stream.Read(_buffer, _bufferCount, toRead);
                _bufferCount += read;
                moreData = toRead == read && read != 0;

                // read packet size
                var bufferIndex = 0;
                while (_bufferCount - bufferIndex >= 4)
                {
                    var packetLength = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(_buffer, bufferIndex + 2));
                    if (packetLength < IPv4Packet.HeaderMinimumLength)
                        throw new Exception($"A packet with invalid length has been received! Length: {packetLength}");

                    // read all packet
                    if (_bufferCount - bufferIndex < packetLength)
                        break;
                    var segment = new ByteArraySegment(_buffer, bufferIndex, packetLength);
                    var ipPacket = new IPv4Packet(segment);
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

            return _ipPackets.ToArray();
        }
    }
}
