namespace VpnHood.Net.Toolkit.Utils;

/// <summary>
/// Represents a value set in a patch, so a patch can tell a property set to <see langword="null"/> from one left out,
/// which a <see langword="null"/> <see cref="Patch{T}"/> stands for.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <param name="value">The value.</param>
public class Patch<T>(T value)
{
    /// <summary>Gets the value.</summary>
    public T Value { get; } = value;

    /// <summary>Returns the value's hash code, or an empty string's for <see langword="null"/>.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return Value?.GetHashCode() ?? "".GetHashCode();
    }

    /// <summary>Returns the value's text, or empty for <see langword="null"/>.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        return Value?.ToString() ?? "";
    }

    /// <summary>
    /// Determines whether the patch equals an object: another patch by its value, anything else with the value itself.
    /// </summary>
    /// <param name="obj">The object.</param>
    /// <returns><see langword="true"/> if they are equal; otherwise, <see langword="false"/>.</returns>
    public override bool Equals(object? obj)
    {
        // another patch by its value, so a patch equals itself; anything else as the value itself would
        return obj is Patch<T> other ? Equals(Value, other.Value) : Equals(Value, obj);
    }

    /// <summary>Wraps a value in a patch.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The patch.</returns>
    public static implicit operator Patch<T>(T value)
    {
        return new Patch<T>(value);
    }

    /// <summary>Unwraps a patch's value.</summary>
    /// <param name="value">The patch.</param>
    /// <returns>The value.</returns>
    /// <exception cref="NullReferenceException">The patch is <see langword="null"/>: the value was not set.</exception>
    public static implicit operator T(Patch<T> value)
    {
        return value != null ? value.Value : throw new NullReferenceException("Value has not been set.");
    }
}