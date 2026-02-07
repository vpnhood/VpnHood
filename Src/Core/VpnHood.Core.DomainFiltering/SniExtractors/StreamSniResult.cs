namespace VpnHood.Core.SniFiltering.SniExtractors;

public class StreamSniResult
{
    public required string? DomainName { get; init; }
    public required Memory<byte> ReadData { get; init; }
}