using System.Buffers;
using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Streams;
using VpnHood.Core.Common.Configuration;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Tunneling;

internal class StreamPacketReader(Stream stream, int bufferSize) : IDisposable
{
    private readonly ReadBufferedStream _stream = new(stream, true, bufferSize);
    private readonly Memory<byte> _minHeader = new byte[20];

    /// <returns>null if read nothing</returns>
    public async Task<IpPacket?> ReadAsync(CancellationToken cancellationToken)
    {
        // read minimum packet header and return null if we reach the end of the stream
        try {
            await _stream.ReadExactlyAsync(_minHeader, cancellationToken).Vhc();
        }
        catch (EndOfStreamException) {
            // if we reach the end of the stream, return null
            // no more packet
            return null;
        }

        // read packet length
        var packetLength = PacketUtil.ReadPacketLength(_minHeader.Span);

        // check packet length
        if (packetLength > TransportDefaults.MaxPacketSize)
            throw new InvalidOperationException(
                $"Packet size exceeds the maximum allowed limit. PacketLength: {packetLength}");

        var rentedArray = ArrayPool<byte>.Shared.Rent(packetLength);
        try {
            // copy the minimum header to the array
            _minHeader.CopyTo(rentedArray);

            // read the rest of the packet
            await _stream.ReadExactlyAsync(rentedArray.AsMemory(_minHeader.Length..packetLength), cancellationToken)
                .Vhc();

            // build the packet, which returns the array when disposed
            return PacketBuilder.Attach(PacketMemory.FromRentedArray(rentedArray, packetLength));
        }
        catch {
            ArrayPool<byte>.Shared.Return(rentedArray);
            throw;
        }
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}