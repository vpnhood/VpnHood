namespace VpnHood.Net.Toolkit.Generics;

/// <summary>
/// Provides the means to create an <see cref="IdName{T}"/> with its type inferred.
/// </summary>
public class IdName
{
    /// <summary>Creates an ID with its display name.</summary>
    /// <typeparam name="T">The type of the ID.</typeparam>
    /// <param name="id">The ID.</param>
    /// <param name="name">The name.</param>
    /// <returns>A new <see cref="IdName{T}"/>.</returns>
    public static IdName<T> Create<T>(T id, string name)
    {
        return new IdName<T>(id, name);
    }
}