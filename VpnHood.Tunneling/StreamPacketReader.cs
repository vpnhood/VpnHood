using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PacketDotNet;
using VpnHood.Common.Utils;

namespace VpnHood.Tunneling;

public class StreamPacketReader : IDisposable
{
    private readonly List<IPPacket> _ipPackets = new();
    private readonly ReadCacheStream _stream;
    private readonly byte[] _packetBuffer = new byte[0xFFFF];

    public StreamPacketReader(Stream stream)
    {
        _stream = new ReadCacheStream(stream, true, 15000); //10 packets
    }


    /// <returns>null if read nothing</returns>
    public async Task<IPPacket[]?> ReadAsync(CancellationToken cancellationToken)
    {
        _ipPackets.Clear();

        while (_ipPackets.Count < 10)
        {
            // read packet header
            const int minPacketSize = 20;
            if (!await StreamUtil.ReadWaitForFillAsync(_stream, _packetBuffer, 0, minPacketSize, cancellationToken))
                return _ipPackets.Count != 0 ? _ipPackets.ToArray() : null;

            // find packet length
            var packetLength = PacketUtil.ReadPacketLength(_packetBuffer, 0);
            if (!await StreamUtil.ReadWaitForFillAsync(_stream, _packetBuffer, minPacketSize, packetLength - minPacketSize, cancellationToken))
                throw new Exception("Stream has been unexpectedly closed before reading the rest of packet.");

            var ipPacket = Packet.ParsePacket(LinkLayers.Raw, _packetBuffer).Extract<IPPacket>();
            _ipPackets.Add(ipPacket);

            // send current packets if there is no more data in cache
            if (!_stream.IsDataAvailableInCache)
                break;
        }

        return _ipPackets.ToArray();
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}