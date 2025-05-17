using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling;

public class StreamPacketReader(Stream stream, int bufferSize)
{
    private readonly ReadCacheStream _stream = new(stream, true, bufferSize);
    private readonly Memory<byte> _packetBuffer = new byte[2000];

    /// <returns>null if read nothing</returns>
    public async Task<IpPacket?> ReadAsync(CancellationToken cancellationToken)
    {

        // read minimum packet header
        const int minPacketSize = 20; // works for ipv4 and ipv6
        await _stream.ReadExactAsync(_packetBuffer[..minPacketSize], cancellationToken);

        // read packet length
        var packetLength = PacketUtil.ReadPacketLength(_packetBuffer.Span);

        // check packet length
        if (packetLength > _packetBuffer.Length) 
            throw new Exception($"Stream has been closed due an oversize packet. PacketLength: {packetLength}");

        // read the rest of the packet
        await _stream.ReadExactAsync(_packetBuffer[minPacketSize..packetLength], cancellationToken);

        // build the packet
        var ipPacket = PacketBuilder.Parse(_packetBuffer.Span[..packetLength]);
        return ipPacket;
    }
}