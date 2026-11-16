namespace VpnHood.Core.Proxies.Management.Abstractions;

/// <summary>
/// Provides the filter and page of a listing of the endpoint store, which the store applies itself.
/// </summary>
/// <remarks>
/// An endpoint is listed when it matches <see cref="Search"/>, when it is enabled or disabled ones are included, and
/// when it is in one of the included groups: those whose last use succeeded, those whose last use failed, and those
/// never used.
/// </remarks>
public class ProxyEndPointStoreListParams
{
    /// <summary>
    /// Gets the text the host or the country code must contain, ignoring case, or <see langword="null"/> for any.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>Gets a value indicating whether endpoints whose last use succeeded are listed.</summary>
    public bool IncludeSucceeded { get; init; } = true;

    /// <summary>Gets a value indicating whether endpoints whose last use failed are listed.</summary>
    public bool IncludeFailed { get; init; } = true;

    /// <summary>Gets a value indicating whether endpoints never used are listed.</summary>
    public bool IncludeUnknown { get; init; } = true;

    /// <summary>Gets a value indicating whether disabled endpoints are listed.</summary>
    public bool IncludeDisabled { get; init; } = true;

    /// <summary>Gets the number of matching endpoints to skip. The default is 0.</summary>
    public int RecordIndex { get; init; }

    /// <summary>Gets the most endpoints to return. The default is <see cref="int.MaxValue"/>.</summary>
    public int RecordCount { get; init; } = int.MaxValue;
}
