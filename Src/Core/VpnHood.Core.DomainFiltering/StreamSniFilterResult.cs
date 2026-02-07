namespace VpnHood.Core.SniFiltering;

public class StreamSniFilterResult
{
    public required DomainFilterAction Action { get; init; }
    public required string? DomainName { get; init; }
    public required Memory<byte> ReadData { get; init; }
}