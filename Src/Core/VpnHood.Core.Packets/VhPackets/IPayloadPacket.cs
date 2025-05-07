namespace VpnHood.Core.Packets.VhPackets;

public interface IPayloadPacket
{
    Memory<byte> Buffer { get; }
}