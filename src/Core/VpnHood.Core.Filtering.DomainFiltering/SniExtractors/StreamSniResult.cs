namespace VpnHood.Core.Filtering.DomainFiltering.SniExtractors;

internal class StreamSniResult
{
    public required string? DomainName { get; init; }
    public required Memory<byte> ReadData { get; init; }
}