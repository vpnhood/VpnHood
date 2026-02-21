namespace VpnHood.Core.Packets;

public interface IPayloadPacket
{
    Memory<byte> Buffer { get; }
}