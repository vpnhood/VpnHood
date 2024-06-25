using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
using VpnHood.Tunneling.Utils;

namespace VpnHood.Tunneling;

public class StreamPacketReader(Stream stream) : IAsyncDisposable
{
    private readonly List<IPPacket> _ipPackets = [];
    private readonly ReadCacheStream _stream = new(stream, true, 15000); // max batch
    private byte[] _packetBuffer = new byte[1600];
    private int _packetBufferCount;


    /// <returns>null if read nothing</returns>
    public async Task<IPPacket[]?> ReadAsync(CancellationToken cancellationToken)
    {
        _ipPackets.Clear();

        while (true)
        {
            // read packet header
            const int minPacketSize = 20;
            if (_packetBufferCount < minPacketSize)
            {
                var toRead = minPacketSize - _packetBufferCount;
                var read = await _stream.ReadAsync(_packetBuffer, _packetBufferCount, toRead, cancellationToken).VhConfigureAwait();
                _packetBufferCount += read;
                
                // is eof?
                if (read == 0 && _packetBufferCount == 0)
                    return null;

                // is unexpected eof?
                if (read == 0)
                    throw new Exception("Stream has been unexpectedly closed before reading the rest of packet.");

                // is uncompleted header?
                if (toRead != read)
                    break; 

                // is just header?
                if (!_stream.DataAvailableInCache)
                    break;
            }

            // find packet length
            var packetLength = PacketUtil.ReadPacketLength(_packetBuffer, 0);
            if (_packetBufferCount < packetLength)
            {
                //not sure if we get any packet more than 1600
                if (packetLength > _packetBuffer.Length)
                {
                    Array.Resize(ref _packetBuffer, packetLength);
                    VhLogger.Instance.LogWarning("Resizing a PacketLength to {packetLength}", packetLength);
                }

                var toRead = packetLength - _packetBufferCount;
                var read = await _stream.ReadAsync(_packetBuffer, _packetBufferCount, toRead, cancellationToken).VhConfigureAwait();
                _packetBufferCount += read;
                if (read == 0)
                    throw new Exception("Stream has been unexpectedly closed before reading the rest of packet.");

                // is packet read?
                if (toRead != read)
                    break; 
            }

            // WARNING: we shouldn't use shared memory for packet
            var packetBuffer = _packetBuffer[.. + packetLength]; //
            var ipPacket = Packet.ParsePacket(LinkLayers.Raw, packetBuffer).Extract<IPPacket>();
           
            _ipPackets.Add(ipPacket);
            _packetBufferCount = 0;

            // Don't try to read more packet if there is no data in cache
            if (!_stream.DataAvailableInCache)
                break;
        }

        return _ipPackets.ToArray();
    }

    public ValueTask DisposeAsync()
    {
        return _stream.DisposeAsync();
    }
}