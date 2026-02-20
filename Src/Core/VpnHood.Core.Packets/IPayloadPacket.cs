namespace VpnHood.Core.Toolkit.Net;

public interface IPayloadPacket
{
    Memory<byte> Buffer { get; }
}