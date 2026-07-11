namespace VpnHood.Core.Common.Messaging;

public class ClientRequestEx
{
    public required ClientRequest Request { get; init; }
    public ReadOnlyMemory<byte> PostBuffer { get; init; } = ReadOnlyMemory<byte>.Empty;
}