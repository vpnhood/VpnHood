namespace VpnHood.Net.Toolkit.Generics;

/// <summary>
/// Represents one page of a list, with the size of the whole list.
/// </summary>
/// <typeparam name="T">The type of the items.</typeparam>
public sealed class ListResult<T>
{
    /// <summary>Gets the page's items.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>Gets the number of items in the whole list.</summary>
    public required int TotalCount { get; init; }
}
