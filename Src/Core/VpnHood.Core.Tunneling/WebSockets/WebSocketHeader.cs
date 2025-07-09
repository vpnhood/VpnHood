namespace VpnHood.Core.Tunneling.WebSockets;

public struct WebSocketHeader()
{
    public const byte FixHeaderLength = 14;
    public required byte HeaderLength { get; init; } 
    public required long PayloadLength { get; init; }
    public byte[] MaskKey { get; init; } = new byte[4];
    public long FixedPayloadLength => PayloadLength - FixHeaderLength;
}
