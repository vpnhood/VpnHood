namespace VpnHood.Core.Filtering.DomainFiltering.SniExtractors;

internal class StreamSniResult
{
    public required string? DomainName { get; init; }
    public required Memory<byte> ReadData { get; init; }

    // Why the stream's name is not known, where it began a ClientHello whose name could not be read; null otherwise.
    // Given, not thrown, with the bytes read: the caller fails the connection only where a rule could match the name
    public Exception? Error { get; init; }
}
