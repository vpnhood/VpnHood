namespace VpnHood.Net.Toolkit.Generics;

/// <summary>
/// Represents an ID with its display name.
/// </summary>
/// <typeparam name="T">The type of the ID.</typeparam>
/// <param name="id">The ID.</param>
/// <param name="name">The name.</param>
public class IdName<T>(T id, string name)
{
    /// <summary>Gets or sets the ID.</summary>
    public T Id { get; set; } = id;

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; } = name;
}
