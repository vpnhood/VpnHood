namespace VpnHood.Core.Packets.VhPackets;

public interface IChecksumPayloadPacket : IPayloadPacket
{
    ushort Checksum { get; }
    void UpdateChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress);
    bool IsChecksumValid(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress);
    ushort ComputeChecksum(ReadOnlySpan<byte> sourceAddress, ReadOnlySpan<byte> destinationAddress);
}