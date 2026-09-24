namespace VpnHood.Net.Packets;

public interface IPayloadPacket
{
    Memory<byte> Buffer { get; }
}