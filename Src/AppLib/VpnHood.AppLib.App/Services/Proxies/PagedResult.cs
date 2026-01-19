namespace VpnHood.AppLib.Services.Proxies;

public sealed class PagedResult<T>
{
    public required T[] Items { get; init; }
    public required int TotalCount { get; init; }
}
