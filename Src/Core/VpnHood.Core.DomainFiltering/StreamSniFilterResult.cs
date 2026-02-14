using VpnHood.Core.Filtering.Abstractions;

namespace VpnHood.Core.DomainFiltering;

public class StreamSniFilterResult
{
    public required FilterAction Action { get; init; }
    public required string? DomainName { get; init; }
    public required Memory<byte> ReadData { get; init; }
}