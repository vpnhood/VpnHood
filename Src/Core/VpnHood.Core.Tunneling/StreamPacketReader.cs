using System.Buffers;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling;

public class StreamPacketReader(Stream stream, int bufferSize)
{
    private readonly ReadCacheStream _stream = new(stream, true, bufferSize);
    private readonly Memory<byte> _minHeader = new byte[20];

    /// <returns>null if read nothing</returns>
    public async Task<IpPacket?> ReadAsync(CancellationToken cancellationToken)
    {
        // read minimum packet header and return null if we reach the end of the stream
        try {
            await _stream.ReadExactlyAsync(_minHeader, cancellationToken);
        }
        catch (EndOfStreamException) {
            // if we reach the end of the stream, return null
            // no more packet
            return null;
        }

        // read packet length
        var packetLength = PacketUtil.ReadPacketLength(_minHeader.Span);

        // check packet length
        if (packetLength > TunnelDefaults.MaxPacketSize)
            throw new InvalidOperationException($"Packet size exceeds the maximum allowed limit. PacketLength: {packetLength}");

        var memoryOwner = MemoryPool<byte>.Shared.Rent(packetLength);
        try {
            // copy the minimum header to the memory owner
            _minHeader.CopyTo(memoryOwner.Memory);

            // read the rest of the packet
            await _stream.ReadExactlyAsync(memoryOwner.Memory[_minHeader.Length..packetLength], cancellationToken);

            // build the packet
            var ipPacket = PacketBuilder.Attach(memoryOwner);
            return ipPacket;
        }
        catch {
            memoryOwner.Dispose();
            throw;
        }

    }
}