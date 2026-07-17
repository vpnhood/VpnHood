namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>Filter, order and page options evaluated inside the endpoint store (SQL side).</summary>
public class ProxyEndPointStoreListParams
{
    /// <summary>Case-insensitive substring match on host or country code.</summary>
    public string? Search { get; init; }

    public bool IncludeSucceeded { get; init; } = true;
    public bool IncludeFailed { get; init; } = true;
    public bool IncludeUnknown { get; init; } = true;
    public bool IncludeDisabled { get; init; } = true;
    public int RecordIndex { get; init; }
    public int RecordCount { get; init; } = int.MaxValue;
}
