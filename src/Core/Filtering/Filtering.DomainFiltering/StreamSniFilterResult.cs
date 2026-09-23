using VpnHood.Core.Filtering.Abstractions;

namespace VpnHood.Core.Filtering.DomainFiltering;

public readonly struct StreamSniFilterResult
{
    public required FilterAction Action { get; init; }
    public required string? DomainName { get; init; }
    public required Memory<byte> ReadData { get; init; }

    public static StreamSniFilterResult Passthrough() => new() {
        Action = FilterAction.Default,
        DomainName = null,
        ReadData = Memory<byte>.Empty
    };
}