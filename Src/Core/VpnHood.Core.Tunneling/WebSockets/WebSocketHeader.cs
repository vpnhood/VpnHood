namespace VpnHood.Core.Tunneling.WebSockets;

public class WebSocketHeader
{
    public required int HeaderLength { get; init; }
    public required long PayloadLength { get; init; }
    public required byte[] MaskKey { get; init; }

}