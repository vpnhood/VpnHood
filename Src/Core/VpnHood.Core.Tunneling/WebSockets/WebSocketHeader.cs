namespace VpnHood.Core.Tunneling.WebSockets;

public readonly struct WebSocketHeader()
{
    public bool IsBinary { get; init; } 
    public bool IsText { get; init; } 
    public bool IsPing { get; init; } 
    public bool IsPong { get; init; } 
    public bool IsCloseConnection { get; init; } 
    public required long PayloadLength { get; init; }
    public ReadOnlyMemory<byte> MaskKey { get; init; }
}
