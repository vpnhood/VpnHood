namespace VpnHood.Core.Common.Messaging;

/// <summary>
/// Represents a client's request with the bytes it sends right after it.
/// </summary>
public class ClientRequestEx
{
    /// <summary>Gets the request.</summary>
    public required ClientRequest Request { get; init; }

    /// <summary>Gets the bytes sent after the request, as a stream's first data. The default is empty.</summary>
    public ReadOnlyMemory<byte> PostBuffer { get; init; } = ReadOnlyMemory<byte>.Empty;
}