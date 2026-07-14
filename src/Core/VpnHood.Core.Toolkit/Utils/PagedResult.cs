namespace VpnHood.Core.Toolkit.Utils;

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int TotalCount { get; init; }
}
