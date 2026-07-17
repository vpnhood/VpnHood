namespace VpnHood.Core.Toolkit.Generics;

public sealed class ListResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int TotalCount { get; init; }
}
